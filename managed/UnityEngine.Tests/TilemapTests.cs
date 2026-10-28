using System;
using System.Collections.Generic;
using UnityEngine.Tilemaps;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A tile that logs the calls a tilemap makes into it.</summary>
    public class CallLogTile : TileBase
    {
        public readonly List<string> log = new List<string>();
        public bool refreshNeighbours;

        public override void RefreshTile(Vector3Int position, ITilemap tilemap)
        {
            log.Add($"{name}.Refresh {position.x},{position.y}");
            base.RefreshTile(position, tilemap);
            if (refreshNeighbours)
            {
                tilemap.RefreshTile(position + Vector3Int.right);
                tilemap.RefreshTile(position + Vector3Int.left);
            }
        }

        public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
        {
            log.Add($"{name}.Data {position.x},{position.y}");
            tileData.color = Color.red;
        }

        public override bool StartUp(Vector3Int position, ITilemap tilemap, GameObject go)
        {
            log.Add($"{name}.StartUp {position.x},{position.y}");
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
    }
}
