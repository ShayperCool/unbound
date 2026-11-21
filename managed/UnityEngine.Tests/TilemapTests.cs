using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Tilemaps;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A tile that logs the calls a tilemap makes into it.</summary>
    public class CallLogTile : TileBase
    {
        public readonly List<string> log = new List<string>();
        /// <summary>A log shared with other tiles, for the order of calls across them.</summary>
        public List<string> shared;
        public bool refreshNeighbours;

        void Log(string call)
        {
            log.Add(call);
            shared?.Add(call);
        }

        public override void RefreshTile(Vector3Int position, ITilemap tilemap)
        {
            Log($"{name}.Refresh {position.x},{position.y}");
            base.RefreshTile(position, tilemap);
            if (refreshNeighbours)
            {
                tilemap.RefreshTile(position + Vector3Int.right);
                tilemap.RefreshTile(position + Vector3Int.left);
            }
        }

        public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
        {
            Log($"{name}.Data {position.x},{position.y}");
            tileData.color = Color.red;
        }

        public override bool StartUp(Vector3Int position, ITilemap tilemap, GameObject go)
        {
            Log($"{name}.StartUp {position.x},{position.y}");
            return true;
        }
    }

    /// <summary>Grids and tilemaps as measured in the Unity Editor (`tests/projects/twod`).</summary>
    public class TilemapTests : IDisposable
    {
        readonly List<Object> m_Made = new List<Object>();

        public void Dispose()
        {
            foreach (var o in m_Made) if (o) Object.DestroyImmediate(o);
        }

        Tilemap NewTilemap(GridLayout.CellLayout layout = GridLayout.CellLayout.Rectangle)
        {
            var grid = new GameObject("Grid", typeof(Grid));
            grid.GetComponent<Grid>().cellLayout = layout;
            m_Made.Add(grid);
            var map = new GameObject("Map", typeof(Tilemap));
            map.transform.SetParent(grid.transform, false);
            return map.GetComponent<Tilemap>();
        }

        T Tile<T>(string name) where T : ScriptableObject
        {
            var t = ScriptableObject.CreateInstance<T>();
            t.name = name;
            m_Made.Add(t);
            return t;
        }

        [Fact]
        public void ArraysOfOtherLengthsAreTakenAsUnityTakesThem()
        {
            // Measured on the 6000.0 Editor: SetTilesBlock takes a longer array's first tiles
            // (pooled arrays), sets nothing from a shorter one; SetTiles sets the pairs both have.
            var map = NewTilemap();
            var a = Tile<Tile>("A");
            var b = Tile<Tile>("B");
            var longer = new TileBase[10];
            for (int i = 0; i < 10; i++) longer[i] = i < 6 ? a : b;
            map.SetTilesBlock(new BoundsInt(0, 0, 0, 3, 2, 1), longer);
            Assert.Same(a, map.GetTile(new Vector3Int(2, 1, 0)));
            Assert.Null(map.GetTile(new Vector3Int(3, 1, 0)));
            map.ClearAllTiles();
            map.SetTilesBlock(new BoundsInt(0, 0, 0, 3, 2, 1), new TileBase[] { a, a, a, a });
            Assert.Null(map.GetTile(new Vector3Int(0, 0, 0)));
            map.SetTiles(new[] { new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0) }, new TileBase[] { a });
            Assert.Same(a, map.GetTile(new Vector3Int(0, 0, 0)));
            Assert.Null(map.GetTile(new Vector3Int(1, 0, 0)));
        }

        [Fact]
        public void HexagonRowsShiftOddRowsAndLocalToCellPicksTheContainingHexagon()
        {
            var map = NewTilemap(GridLayout.CellLayout.Hexagon);
            Assert.Equal(new Vector3(0.5f, 0.75f, 0f), map.CellToLocal(new Vector3Int(0, 1, 0)));
            Assert.Equal(new Vector3(0.5f, -0.75f, 0f), map.CellToLocal(new Vector3Int(0, -1, 0)));
            Assert.Equal(new Vector3Int(-1, -2, 0), map.LocalToCell(new Vector3(-1.05f, -1.05f, 0f)));
            // On a shared edge, the higher row wins.
            Assert.Equal(new Vector3Int(1, 0, 0), map.LocalToCell(new Vector3(0.7f, -0.35f, 0f)));
        }

        [Fact]
        public void IsometricCellsStepAlongTheDiagonals()
        {
            var map = NewTilemap(GridLayout.CellLayout.Isometric);
            Assert.Equal(new Vector3(0.5f, 0.5f, 0f), map.CellToLocal(new Vector3Int(1, 0, 0)));
            Assert.Equal(new Vector3(-0.5f, 0.5f, 0f), map.CellToLocal(new Vector3Int(0, 1, 0)));
            Assert.Equal(new Vector3Int(1, 0, 0), map.LocalToCell(new Vector3(0.6f, 1.35f, 0f)));
        }

        [Fact]
        public void BoundsGrowFromTheOriginAndShrinkOnlyWhenCompressed()
        {
            var map = NewTilemap();
            var tile = Tile<Tile>("t");
            map.SetTile(new Vector3Int(2, 3, 0), tile);
            Assert.Equal(new BoundsInt(0, 0, 0, 3, 4, 1), map.cellBounds);
            map.SetTile(new Vector3Int(-1, -2, 0), tile);
            map.SetTile(new Vector3Int(2, 3, 0), null);
            Assert.Equal(new BoundsInt(-1, -2, 0, 4, 6, 1), map.cellBounds);
            map.CompressBounds();
            Assert.Equal(new BoundsInt(-1, -2, 0, 1, 1, 1), map.cellBounds);
        }

        [Fact]
        public void SettingTilesRefreshesOldAndNewThenAsksEachRefreshedCellInOrder()
        {
            var map = NewTilemap();
            var a = Tile<CallLogTile>("A");
            var b = Tile<CallLogTile>("B");
            b.refreshNeighbours = true;
            map.SetTile(new Vector3Int(0, 0, 0), a);
            a.log.Clear();
            map.SetTile(new Vector3Int(1, 0, 0), b);
            var log = new List<string>(a.log);
            Assert.Equal(new[] { "A.Data 0,0", "A.StartUp 0,0" }, log);
            Assert.Equal(new[] { "B.Refresh 1,0", "B.Data 1,0", "B.StartUp 1,0" }, b.log);
        }

        [Fact]
        public void LockedColorsComeFromTheTileAndUnlockedOnesSurviveARefresh()
        {
            var map = NewTilemap();
            var tile = Tile<Tile>("t");
            tile.flags = TileFlags.None;
            tile.color = new Color(1f, 0.5f, 0.5f, 1f);
            var p = new Vector3Int(1, 1, 0);
            map.SetTile(p, tile);
            Assert.Equal(new Color(1f, 0.5f, 0.5f, 1f), map.GetColor(p));
            map.SetColor(p, Color.green);
            map.RefreshTile(p);
            Assert.Equal(Color.green, map.GetColor(p));
            tile.flags = TileFlags.LockColor;
            map.RefreshTile(p);
            Assert.Equal(new Color(1f, 0.5f, 0.5f, 1f), map.GetColor(p));
            map.SetColor(p, Color.green);
            Assert.Equal(new Color(1f, 0.5f, 0.5f, 1f), map.GetColor(p));
        }

        [Fact]
        public void BoxFillStaysInsideTheBoundsGrownToThePosition()
        {
            var map = NewTilemap();
            var red = Tile<Tile>("red");
            var green = Tile<Tile>("green");
            map.SetTile(new Vector3Int(1, 0, 0), red);
            map.BoxFill(new Vector3Int(0, 1, 0), green, -1, -1, 1, 3);
            Assert.Equal(new BoundsInt(0, 0, 0, 2, 2, 1), map.cellBounds);
            Assert.Equal(new TileBase[] { green, red, green, green }, map.GetTilesBlock(map.cellBounds));
        }

        [Fact]
        public void EditorPreviewTilesStaySeparateFromActualTilesAndSkipStartup()
        {
            var map = NewTilemap();
            var actual = Tile<Tile>("actual");
            actual.color = Color.red;
            actual.flags = TileFlags.LockColor;
            var preview = Tile<CallLogTile>("preview");
            preview.refreshNeighbours = true;
            var neighbor = Tile<CallLogTile>("neighbor");

            var p = new Vector3Int(1, 1, 0);
            var pNbr = new Vector3Int(2, 1, 0);
            map.SetTile(p, actual);
            map.SetTile(pNbr, neighbor);
            neighbor.log.Clear();

            int tileEvents = 0, posEvents = 0;
            Action<Tilemap, Tilemap.SyncTile[]> onTile = (_, _) => tileEvents++;
            Action<Tilemap, Unity.Collections.NativeArray<Vector3Int>> onPos = (_, _) => posEvents++;
            Tilemap.tilemapTileChanged += onTile;
            Tilemap.tilemapPositionsChanged += onPos;
            try
            {
                map.SetEditorPreviewTile(p, preview);
                Assert.True(map.HasEditorPreviewTile(p));
                Assert.Same(preview, map.GetEditorPreviewTile(p));
                Assert.Same(preview, map.GetEditorPreviewTile<CallLogTile>(p));
                Assert.Same(actual, map.GetTile(p));
                Assert.Equal(Color.red, map.GetEditorPreviewColor(p));
                // Replacing an underlying actual tile refreshes old (calling GetTileData on preview)
                // then new (RefreshTile + GetTileData on preview), with no StartUp.
                Assert.Equal(new[] { "preview.Data 1,1", "preview.Refresh 1,1", "preview.Data 1,1" }, preview.log);
                Assert.Equal(new[] { "neighbor.Data 2,1", "neighbor.StartUp 2,1" }, neighbor.log);

                // Setting a preview tile on an empty cell runs only new.RefreshTile + GetTileData.
                var emptyPos = new Vector3Int(0, 3, 0);
                preview.log.Clear();
                map.SetEditorPreviewTile(emptyPos, preview);
                Assert.Equal(new[] { "preview.Refresh 0,3", "preview.Data 0,3" }, preview.log);
                map.SetEditorPreviewTile(emptyPos, null);

                // Repeating the same preview tile is a no-op.
                preview.log.Clear();
                map.SetEditorPreviewTile(p, preview);
                Assert.Empty(preview.log);

                // Clearing preview restores default empty getters while preserving actual tile.
                map.SetEditorPreviewTile(p, null);
                Assert.False(map.HasEditorPreviewTile(p));
                Assert.Null(map.GetEditorPreviewTile(p));
                Assert.Equal(Color.white, map.GetEditorPreviewColor(p));
                Assert.Equal(Matrix4x4.identity, map.GetEditorPreviewTransformMatrix(p));
                Assert.Equal(TileFlags.None, map.GetEditorPreviewTileFlags(p));
                Assert.Same(actual, map.GetTile(p));
                Assert.Equal(0, tileEvents);
                Assert.Equal(0, posEvents);
            }
            finally
            {
                Tilemap.tilemapTileChanged -= onTile;
                Tilemap.tilemapPositionsChanged -= onPos;
            }
        }

        [Fact]
        public void EditorPreviewColorAndTransformRespectLocksAndRequirePreviewTile()
        {
            var map = NewTilemap();
            var free = Tile<Tile>("free");
            free.flags = TileFlags.None;
            free.color = Color.green;
            var lockColor = Tile<Tile>("lc");
            lockColor.flags = TileFlags.LockColor;
            lockColor.color = Color.blue;
            var lockTransform = Tile<Tile>("lt");
            lockTransform.flags = TileFlags.LockTransform;
            lockTransform.transform = Matrix4x4.Scale(new Vector3(2f, 2f, 1f));

            var customColor = new Color(0.25f, 0.5f, 0.75f, 0.5f);
            var customMatrix = Matrix4x4.Translate(new Vector3(3f, 4f, 0f));

            // Actual-only cell: preview setters are no-ops and do not alter actual tile.
            var pAct = new Vector3Int(0, 0, 0);
            map.SetTile(pAct, free);
            map.SetEditorPreviewColor(pAct, customColor);
            map.SetEditorPreviewTransformMatrix(pAct, customMatrix);
            Assert.False(map.HasEditorPreviewTile(pAct));
            Assert.Equal(Color.green, map.GetColor(pAct));
            Assert.Equal(Matrix4x4.identity, map.GetTransformMatrix(pAct));

            var pFree = new Vector3Int(1, 0, 0);
            map.SetEditorPreviewTile(pFree, free);
            map.SetEditorPreviewColor(pFree, customColor);
            map.SetEditorPreviewTransformMatrix(pFree, customMatrix);
            Assert.Equal(customColor, map.GetEditorPreviewColor(pFree));
            Assert.Equal(customMatrix, map.GetEditorPreviewTransformMatrix(pFree));

            var pLC = new Vector3Int(2, 0, 0);
            map.SetEditorPreviewTile(pLC, lockColor);
            map.SetEditorPreviewColor(pLC, customColor);
            map.SetEditorPreviewTransformMatrix(pLC, customMatrix);
            Assert.Equal(Color.blue, map.GetEditorPreviewColor(pLC));
            Assert.Equal(customMatrix, map.GetEditorPreviewTransformMatrix(pLC));

            var pLT = new Vector3Int(3, 0, 0);
            map.SetEditorPreviewTile(pLT, lockTransform);
            map.SetEditorPreviewColor(pLT, customColor);
            map.SetEditorPreviewTransformMatrix(pLT, customMatrix);
            Assert.Equal(customColor, map.GetEditorPreviewColor(pLT));
            Assert.Equal(Matrix4x4.Scale(new Vector3(2f, 2f, 1f)), map.GetEditorPreviewTransformMatrix(pLT));
        }

        [Fact]
        public void EditorPreviewBoxAndFloodFillUsePreviewBoundsAndActualTargets()
        {
            var map = NewTilemap();
            var a = Tile<Tile>("A");
            var wall = Tile<Tile>("Wall");
            var prev = Tile<Tile>("Prev");
            var other = Tile<Tile>("Other");

            map.SetTile(new Vector3Int(0, 0, 0), a);
            map.SetTile(new Vector3Int(1, 0, 0), a);
            map.SetTile(new Vector3Int(0, 1, 0), a);
            map.SetTile(new Vector3Int(2, 0, 0), wall);
            map.SetTile(new Vector3Int(2, 1, 0), wall);
            map.SetTile(new Vector3Int(3, 0, 0), a);

            // Inverted box does nothing and does not grow preview bounds.
            map.EditorPreviewBoxFill(new Vector3Int(5, 5, 0), prev, 3, 0, 1, 2);
            Assert.Equal(new Vector3Int(4, 2, 1), map.editorPreviewSize);
            Assert.False(map.HasEditorPreviewTile(new Vector3Int(5, 5, 0)));

            // Sync preview bounds via ClearAllEditorPreviewTiles, then flood/box fill respects wall.
            map.ClearAllEditorPreviewTiles();
            map.EditorPreviewBoxFill(new Vector3Int(0, 0, 0), prev, 0, 0, 3, 1);
            Assert.Same(prev, map.GetEditorPreviewTile(new Vector3Int(0, 0, 0)));
            Assert.Same(prev, map.GetEditorPreviewTile(new Vector3Int(1, 0, 0)));
            Assert.Same(prev, map.GetEditorPreviewTile(new Vector3Int(0, 1, 0)));
            Assert.False(map.HasEditorPreviewTile(new Vector3Int(3, 0, 0)));

            // Non-TileBase Object is accepted without throwing and sets HasEditorPreviewTile.
            var tex = new Texture2D(2, 2);
            m_Made.Add(tex);
            map.ClearAllEditorPreviewTiles();
            map.EditorPreviewBoxFill(new Vector3Int(0, 0, 0), tex, 0, 0, 1, 0);
            Assert.True(map.HasEditorPreviewTile(new Vector3Int(0, 0, 0)));
            Assert.Null(map.GetEditorPreviewTile(new Vector3Int(0, 0, 0)));

            // Flood fill with null at an empty far point still grows preview bounds without filling.
            map.ClearAllEditorPreviewTiles();
            map.EditorPreviewFloodFill(new Vector3Int(6, 5, 0), null);
            Assert.Equal(new Vector3Int(7, 6, 1), map.editorPreviewSize);
            Assert.False(map.HasEditorPreviewTile(new Vector3Int(6, 5, 0)));

            // Z-layer isolation and null clear over preview overlays.
            map.ClearAllEditorPreviewTiles();
            map.SetTile(new Vector3Int(-2, -1, 3), a);
            map.SetTile(new Vector3Int(-1, -1, 3), a);
            map.ClearAllEditorPreviewTiles();
            map.EditorPreviewFloodFill(new Vector3Int(-2, -1, 3), other);
            Assert.Same(other, map.GetEditorPreviewTile(new Vector3Int(-2, -1, 3)));
            Assert.Same(other, map.GetEditorPreviewTile(new Vector3Int(-1, -1, 3)));
            Assert.False(map.HasEditorPreviewTile(new Vector3Int(-2, -1, 0)));
            map.EditorPreviewFloodFill(new Vector3Int(-2, -1, 3), null);
            Assert.False(map.HasEditorPreviewTile(new Vector3Int(-2, -1, 3)));
            Assert.False(map.HasEditorPreviewTile(new Vector3Int(-1, -1, 3)));
        }

        [Fact]
        public void EditorPreviewBoundsInteractWithClearCompressResizeAndClearAll()
        {
            var map = NewTilemap();
            var a = Tile<Tile>("A");
            var prev = Tile<Tile>("Prev");

            map.SetTile(new Vector3Int(1, 1, 0), a);
            map.SetTile(new Vector3Int(3, 3, 0), a);
            map.SetEditorPreviewTile(new Vector3Int(-2, 5, 0), prev);
            Assert.Equal(new Vector3Int(-2, 0, 0), map.editorPreviewOrigin);
            Assert.Equal(new Vector3Int(4, 6, 1), map.editorPreviewSize);

            map.ClearAllEditorPreviewTiles();
            Assert.False(map.HasEditorPreviewTile(new Vector3Int(-2, 5, 0)));
            Assert.Equal(new Vector3Int(0, 0, 0), map.editorPreviewOrigin);
            Assert.Equal(new Vector3Int(4, 4, 1), map.editorPreviewSize);
            Assert.Same(a, map.GetTile(new Vector3Int(1, 1, 0)));

            // CompressBounds resets preview bounds to compressed actual bounds while keeping preview cells.
            map.SetTile(new Vector3Int(3, 3, 0), null);
            map.SetEditorPreviewTile(new Vector3Int(5, 5, 0), prev);
            map.CompressBounds();
            Assert.Equal(new Vector3Int(1, 1, 0), map.editorPreviewOrigin);
            Assert.Equal(new Vector3Int(1, 1, 1), map.editorPreviewSize);
            Assert.True(map.HasEditorPreviewTile(new Vector3Int(5, 5, 0)));

            // ClearAllTiles clears both actual and preview cells and resets both bounds.
            map.ClearAllTiles();
            Assert.False(map.HasEditorPreviewTile(new Vector3Int(5, 5, 0)));
            Assert.Equal(new Vector3Int(0, 0, 0), map.editorPreviewOrigin);
            Assert.Equal(new Vector3Int(0, 0, 1), map.editorPreviewSize);
        }

        [Fact]
        public void EditorPreviewApisWorkUnderUnmeasuredReleaseProfile()
        {
            using (TestRelease.Pin(TestRelease.Unmeasured))
            {
                var map = NewTilemap();
                var tile = Tile<Tile>("T");
                tile.color = Color.cyan;
                var p = new Vector3Int(2, 3, 0);
                map.SetEditorPreviewTile(p, tile);
                Assert.True(map.HasEditorPreviewTile(p));
                Assert.Same(tile, map.GetEditorPreviewTile(p));
                Assert.Equal(Color.cyan, map.GetEditorPreviewColor(p));
                map.ClearAllEditorPreviewTiles();
                Assert.False(map.HasEditorPreviewTile(p));
            }
        }

        // The order a fill visits a 3x3 region from its centre (tests/projects/tilemappreview 07.fillOrder).
        static readonly string[] s_CentreOrder = { "1,1", "2,1", "2,2", "1,2", "0,2", "0,1", "0,0", "1,0", "2,0" };

        static List<string> Refreshed(CallLogTile tile) =>
            tile.log.Where(l => l.Contains(".Refresh ")).Select(l => l.Substring(l.IndexOf(' ') + 1)).ToList();

        [Fact]
        public void EveryFillVisitsCellsInOneDepthFirstOrderWhateverTheBoxSize()
        {
            var fills = new (string Name, Action<Tilemap, TileBase> Fill)[]
            {
                ("BoxFill", (m, t) => m.BoxFill(new Vector3Int(1, 1, 0), t, -5000, -5000, 5000, 5000)),
                ("FloodFill", (m, t) => m.FloodFill(new Vector3Int(1, 1, 0), t)),
                ("EditorPreviewBoxFill", (m, t) => m.EditorPreviewBoxFill(new Vector3Int(1, 1, 0), t, -5000, -5000, 5000, 5000)),
                ("EditorPreviewFloodFill", (m, t) => m.EditorPreviewFloodFill(new Vector3Int(1, 1, 0), t)),
            };
            foreach (bool farBounds in new[] { false, true })
                foreach (var (name, fill) in fills)
                {
                    // A 3x3 region of A inside walls; with far tiles the bounds (and the box) span
                    // millions of cells while the region stays small.
                    var map = NewTilemap();
                    var a = Tile<Tile>("A");
                    var wall = Tile<Tile>("Wall");
                    for (int y = -1; y <= 3; y++)
                        for (int x = -1; x <= 3; x++)
                            map.SetTile(new Vector3Int(x, y, 0), x < 0 || x > 2 || y < 0 || y > 2 ? wall : a);
                    if (farBounds)
                    {
                        map.SetTile(new Vector3Int(-3000, -3000, 0), wall);
                        map.SetTile(new Vector3Int(3000, 3000, 0), wall);
                    }
                    map.ClearAllEditorPreviewTiles();
                    var filled = Tile<CallLogTile>("F");
                    fill(map, filled);
                    Assert.True(s_CentreOrder.SequenceEqual(Refreshed(filled)), $"{name}, far bounds {farBounds}: {string.Join(" ", Refreshed(filled))}");
                }
        }

        static Vector3Int[] Row(int count) => Enumerable.Range(0, count).Select(i => new Vector3Int(count - 1 - i, 0, 0)).ToArray();

        [Fact]
        public void ChangesOfUpToFourCellsRefreshCellByCellAndLargerOnesInRequestOrderDataFirst()
        {
            var four = Tile<CallLogTile>("F");
            NewTilemap().SetTiles(Row(4), Enumerable.Repeat<TileBase>(four, 4).ToArray());
            Assert.Equal(new[]
            {
                "F.Refresh 3,0", "F.Refresh 2,0", "F.Refresh 1,0", "F.Refresh 0,0",
                "F.Data 0,0", "F.StartUp 0,0", "F.Data 1,0", "F.StartUp 1,0",
                "F.Data 2,0", "F.StartUp 2,0", "F.Data 3,0", "F.StartUp 3,0",
            }, four.log);

            // Five cells over a filled row, each new tile also asking for its neighbours: the cells
            // refresh in the order first asked for (4, 5, 3, 2, 1, 0, -1), every GetTileData first.
            var map = NewTilemap();
            var shared = new List<string>();
            var old = Tile<CallLogTile>("A");
            old.shared = shared;
            for (int x = -1; x <= 5; x++) map.SetTile(new Vector3Int(x, 0, 0), old);
            shared.Clear();
            var five = Tile<CallLogTile>("B");
            five.shared = shared;
            five.refreshNeighbours = true;
            map.SetTiles(Row(5), Enumerable.Repeat<TileBase>(five, 5).ToArray());
            Assert.Equal(new[]
            {
                "A.Refresh 4,0", "B.Refresh 4,0", "A.Refresh 3,0", "B.Refresh 3,0", "A.Refresh 2,0", "B.Refresh 2,0",
                "A.Refresh 1,0", "B.Refresh 1,0", "A.Refresh 0,0", "B.Refresh 0,0",
                "B.Data 4,0", "A.Data 5,0", "B.Data 3,0", "B.Data 2,0", "B.Data 1,0", "B.Data 0,0", "A.Data -1,0",
                "B.StartUp 4,0", "A.StartUp 5,0", "B.StartUp 3,0", "B.StartUp 2,0", "B.StartUp 1,0", "B.StartUp 0,0", "A.StartUp -1,0",
            }, shared);
        }

        [Fact]
        public void RefreshTileRefreshesThePreviewTileThenTheActualTile()
        {
            var map = NewTilemap();
            var shared = new List<string>();
            var actual = Tile<CallLogTile>("A");
            actual.shared = shared;
            var preview = Tile<CallLogTile>("P");
            preview.shared = shared;
            map.SetTile(new Vector3Int(0, 0, 0), actual);
            map.SetEditorPreviewTile(new Vector3Int(0, 0, 0), preview);
            shared.Clear();
            map.RefreshTile(new Vector3Int(0, 0, 0));
            Assert.Equal(new[] { "P.Data 0,0", "A.Data 0,0", "A.StartUp 0,0" }, shared);

            // Clearing the preview refreshes cell by cell at once: the old preview tile's refresh,
            // then the actual tile's.
            map.SetTile(new Vector3Int(1, 0, 0), actual);
            map.SetEditorPreviewTile(new Vector3Int(1, 0, 0), preview);
            shared.Clear();
            map.ClearAllEditorPreviewTiles();
            Assert.Equal(new[]
            {
                "P.Refresh 0,0", "A.Data 0,0", "A.StartUp 0,0", "A.Refresh 0,0", "A.Data 0,0", "A.StartUp 0,0",
                "P.Refresh 1,0", "A.Data 1,0", "A.StartUp 1,0", "A.Refresh 1,0", "A.Data 1,0", "A.StartUp 1,0",
            }, shared);
        }

        [Fact]
        public void CompressBoundsKeepEditorPreviewLeavesThePreviewBounds()
        {
            var map = NewTilemap();
            var a = Tile<Tile>("A");
            var prev = Tile<Tile>("Prev");
            map.SetTile(new Vector3Int(1, 1, 0), a);
            map.SetTile(new Vector3Int(3, 3, 0), a);
            map.SetTile(new Vector3Int(3, 3, 0), null);
            map.SetEditorPreviewTile(new Vector3Int(-2, 5, 0), prev);
            map.CompressBoundsKeepEditorPreview();
            Assert.Equal(new Vector3Int(1, 1, 0), map.origin);
            Assert.Equal(new Vector3Int(1, 1, 1), map.size);
            // The preview's own bounds stay (-2,0,0) + (2,6,1); the reported ones combine both layers.
            Assert.Equal(new Vector3Int(-2, 0, 0), map.editorPreviewOrigin);
            Assert.Equal(new Vector3Int(2, 6, 1), map.editorPreviewSize);
            Assert.True(map.HasEditorPreviewTile(new Vector3Int(-2, 5, 0)));
        }

        [Fact]
        public void ChangeCallbacksReportEveryCellTheTilesAskedToRefresh()
        {
            var map = NewTilemap();
            var tile = Tile<CallLogTile>("B");
            tile.refreshNeighbours = true;
            var reported = new List<Vector3Int>();
            Action<Tilemap, Tilemap.SyncTile[]> onChanged = (m, tiles) =>
            {
                if (m == map) reported.AddRange(tiles.Select(t => t.position));
            };
            Tilemap.tilemapTileChanged += onChanged;
            try
            {
                // The empty neighbours (-1,0) and (2,0) are reported too, in z, y, x order.
                map.SetTiles(Row(2), new TileBase[] { tile, tile });
            }
            finally
            {
                Tilemap.tilemapTileChanged -= onChanged;
            }
            Assert.Equal(new[] { new Vector3Int(-1, 0, 0), new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), new Vector3Int(2, 0, 0) }, reported);
        }

        /// <summary>Logs what the tilemap answers, from GetTileData, for the cell to the right.</summary>
        public class RightViewTile : TileBase
        {
            public readonly List<string> seen = new List<string>();

            public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
            {
                var right = position + Vector3Int.right;
                seen.Add($"{tilemap.GetTile(position)?.name}|{tilemap.GetTile(right)?.name}|{tilemap.GetColor(right)}");
            }
        }

        [Fact]
        public void TilesSeeThePreviewLayerThroughITilemapWhicheverLayerRefreshes()
        {
            var map = NewTilemap();
            var actual = Tile<Tile>("Actual");
            actual.color = Color.green;
            var preview = Tile<Tile>("Preview");
            preview.color = Color.blue;
            map.SetTile(new Vector3Int(1, 0, 0), actual);
            map.SetEditorPreviewTile(new Vector3Int(1, 0, 0), preview);
            var viewer = Tile<RightViewTile>("A");
            map.SetTile(new Vector3Int(0, 0, 0), viewer);
            viewer.seen.Clear();
            map.RefreshTile(new Vector3Int(0, 0, 0));
            Assert.Equal(new[] { $"A|Preview|{Color.blue}" }, viewer.seen);

            // With a preview tile on its own cell, the actual tile sees that preview tile.
            var overlay = Tile<RightViewTile>("V");
            map.SetEditorPreviewTile(new Vector3Int(0, 0, 0), overlay);
            viewer.seen.Clear();
            map.RefreshTile(new Vector3Int(0, 0, 0));
            Assert.Equal(new[] { $"V|Preview|{Color.blue}" }, viewer.seen);
        }

        [Fact]
        public void APreviewTileEqualToTheActualTileTakesItsData()
        {
            var map = NewTilemap();
            var tile = Tile<Tile>("T");
            tile.color = Color.green;
            tile.flags = TileFlags.None;
            map.SetTile(Vector3Int.zero, tile);
            map.SetEditorPreviewTile(Vector3Int.zero, tile);
            Assert.True(map.HasEditorPreviewTile(Vector3Int.zero));
            Assert.Equal(Color.green, map.GetEditorPreviewColor(Vector3Int.zero));

            // The tile asks for its refresh once, though it is both the old and the new tile.
            var logged = Tile<CallLogTile>("L");
            map.SetTile(Vector3Int.right, logged);
            logged.log.Clear();
            map.SetEditorPreviewTile(Vector3Int.right, logged);
            Assert.Equal(new[] { "L.Refresh 1,0", "L.Data 1,0", "L.Data 1,0", "L.StartUp 1,0" }, logged.log);
        }

        [Fact]
        public void ADestroyedPreviewTileShowsTheActualTileToNeighbours()
        {
            var map = NewTilemap();
            var actual = Tile<Tile>("Actual");
            var doomed = ScriptableObject.CreateInstance<Tile>();
            doomed.color = Color.blue;
            map.SetTile(Vector3Int.zero, actual);
            map.SetEditorPreviewTile(Vector3Int.zero, doomed);
            Object.DestroyImmediate(doomed);
            var viewer = Tile<RightViewTile>("W");
            map.SetTile(Vector3Int.left, viewer);
            viewer.seen.Clear();
            map.RefreshTile(Vector3Int.left);
            Assert.Equal(new[] { $"W|Actual|{Color.white}" }, viewer.seen);
            Assert.False(map.HasEditorPreviewTile(Vector3Int.zero));
            // The cell keeps the data the destroyed tile gave.
            Assert.Equal(Color.blue, map.GetEditorPreviewColor(Vector3Int.zero));
        }
    }
}
