using System;
using System.Collections.Generic;
using Unity.Collections;
using Unbound.Runtime;

namespace UnityEngine.Tilemaps
{
    /// <summary>
    /// Tiles on the cells of the grid above. Each cell keeps its tile and the data the tile gave
    /// for it (sprite, color, transform, flags, collider type, object to instantiate, animation).
    ///
    /// What Unity does, measured in `tests/projects/twod`: setting tiles first lets the old then
    /// the new tile of each changed cell refresh (collecting positions, <see cref="TileBase.RefreshTile"/>),
    /// then each collected cell that has a tile, in z, y, x order, asks its tile for data,
    /// StartUp and animation data. A cell taking a tile for the first time takes all of the data;
    /// later refreshes take the color only under LockColor and the transform only under
    /// LockTransform, so values set on the cell survive. Bounds grow to include the origin and
    /// every set cell and shrink only on CompressBounds/ClearAllTiles. The change callbacks fire
    /// synchronously for cells whose tile, sprite, color or transform changed.
    /// </summary>
    [ClassId(1839735485)]
    [RequireComponent(typeof(Transform))]
    public sealed partial class Tilemap : GridLayout
    {
        internal sealed class Cell
        {
            internal TileBase Tile;
            internal Sprite Sprite;
            internal Color Color = Color.white;
            internal Matrix4x4 Transform = Matrix4x4.identity;
            internal GameObject ObjectToInstantiate;
            internal GameObject Instantiated;
            internal TileFlags Flags;
            internal Tile.ColliderType Collider;
            /// <summary>Took no data from its tile yet.</summary>
            internal bool Fresh = true;
            internal Sprite[] Frames;
            internal float AnimationSpeed = 1f;
            internal float AnimationStartTime;
            internal TileAnimationFlags AnimationFlags;
            internal int Frame = -1;
            internal float AnimationTime;

            internal Cell Copy() => (Cell)MemberwiseClone();
        }

        /// <summary>Cells in Unity's order: z, then y, then x.</summary>
        internal sealed class CellOrder : IComparer<Vector3Int>
        {
            internal static readonly CellOrder Instance = new CellOrder();

            public int Compare(Vector3Int a, Vector3Int b)
            {
                int c = a.z.CompareTo(b.z);
                if (c != 0) return c;
                c = a.y.CompareTo(b.y);
                return c != 0 ? c : a.x.CompareTo(b.x);
            }
        }

        internal readonly SortedDictionary<Vector3Int, Cell> m_Cells = new SortedDictionary<Vector3Int, Cell>(CellOrder.Instance);
        Vector3Int m_Origin;
        Vector3Int m_Size = new Vector3Int(0, 0, 1);
        /// <summary>The z extent localBounds reports (Unity keeps it until the tiles are cleared).</summary>
        int m_LocalZMin, m_LocalZMax = 1;
        Color m_Color = Color.white;
        Vector3 m_TileAnchor = new Vector3(0.5f, 0.5f, 0f);
        Orientation m_Orientation;
        Matrix4x4 m_OrientationMatrix = Matrix4x4.identity;
        float m_AnimationFrameRate = 1f;
        ITilemap m_ITilemap;
        bool m_BufferSyncTile;
        /// <summary>Bumped when what the tilemap draws changes.</summary>
        internal int m_RenderVersion;
        /// <summary>Bumped when what colliders are built from changes.</summary>
        internal int m_ShapeVersion;
        /// <summary>Cells whose collider-relevant data changed since colliders last looked.</summary>
        internal readonly ChangeList m_ShapeChanges = new ChangeList();

        /// <summary>Cells in the order they last changed.</summary>
        internal sealed class ChangeList : IEnumerable<Vector3Int>
        {
            readonly List<Vector3Int> m_Order = new List<Vector3Int>();
            readonly HashSet<Vector3Int> m_Set = new HashSet<Vector3Int>();
            internal int Count => m_Order.Count;
            internal void Add(Vector3Int p)
            {
                if (!m_Set.Add(p)) m_Order.Remove(p);
                m_Order.Add(p);
            }
            internal void Clear() { m_Order.Clear(); m_Set.Clear(); }
            public IEnumerator<Vector3Int> GetEnumerator() => m_Order.GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public Tilemap() { }

        ITilemap Itilemap => m_ITilemap ??= new ITilemap { m_Tilemap = this };

        internal override Grid LayoutSource => layoutGrid;

        public Grid layoutGrid
        {
            get
            {
                for (var t = transform.parent; t != null; t = t.parent)
                {
                    var grid = t.GetComponent<Grid>();
                    if (grid != null) return grid;
                }
                return null;
            }
        }

        internal void LayoutChanged()
        {
            m_RenderVersion++;
            m_ShapeVersion++;
        }

        // ------------------------------------------------------------------ properties

        public BoundsInt cellBounds => new BoundsInt(m_Origin, m_Size);

        public Bounds localBounds
        {
            get
            {
                if (cellLayout == CellLayout.Hexagon) return GetBoundsLocal(m_Origin, m_Size);
                var b = GetBoundsLocal(new Vector3(m_Origin.x, m_Origin.y, 0f), new Vector3(m_Size.x, m_Size.y, 0f));
                var center = b.center;
                var size = b.size;
                center.z = (m_LocalZMin + m_LocalZMax) * 0.5f;
                size.z = m_LocalZMax - m_LocalZMin;
                return new Bounds(center, size);
            }
        }

        internal Bounds localFrameBounds => localBounds;

        public float animationFrameRate
        {
            get => m_AnimationFrameRate;
            set => m_AnimationFrameRate = value;
        }

        public Color color
        {
            get => m_Color;
            set
            {
                m_Color = value;
                m_RenderVersion++;
            }
        }

        public Vector3Int origin
        {
            get => m_Origin;
            set
            {
                m_Origin = value;
                m_RenderVersion++;
            }
        }

        public Vector3Int size
        {
            get => m_Size;
            set
            {
                m_Size = value;
                m_RenderVersion++;
            }
        }

        public Vector3 tileAnchor
        {
            get => m_TileAnchor;
            set
            {
                m_TileAnchor = value;
                m_RenderVersion++;
                m_ShapeVersion++;
            }
        }

        public Orientation orientation
        {
            get => m_Orientation;
            set
            {
                m_Orientation = value;
                if (value != Orientation.Custom) m_OrientationMatrix = OrientationMatrixOf(value);
                m_RenderVersion++;
            }
        }

        public Matrix4x4 orientationMatrix
        {
            get => m_OrientationMatrix;
            set
            {
                // Unity keeps the orientation as it was (measured).
                m_OrientationMatrix = value;
                m_RenderVersion++;
            }
        }

        internal static Matrix4x4 OrientationMatrixOf(Orientation orientation)
        {
            // Rows of each orientation's matrix (measured).
            switch (orientation)
            {
                case Orientation.XZ: return Rows(new Vector3(1, 0, 0), new Vector3(0, 0, -1), new Vector3(0, 1, 0));
                case Orientation.YX: return Rows(new Vector3(0, 1, 0), new Vector3(1, 0, 0), new Vector3(0, 0, -1));
                case Orientation.YZ: return Rows(new Vector3(0, 0, 1), new Vector3(1, 0, 0), new Vector3(0, 1, 0));
                case Orientation.ZX: return Rows(new Vector3(0, 1, 0), new Vector3(0, 0, 1), new Vector3(1, 0, 0));
                case Orientation.ZY: return Rows(new Vector3(0, 0, -1), new Vector3(0, 1, 0), new Vector3(1, 0, 0));
                default: return Matrix4x4.identity;
            }
        }

        static Matrix4x4 Rows(Vector3 r0, Vector3 r1, Vector3 r2)
        {
            var m = Matrix4x4.identity;
            m.SetRow(0, new Vector4(r0.x, r0.y, r0.z, 0f));
            m.SetRow(1, new Vector4(r1.x, r1.y, r1.z, 0f));
            m.SetRow(2, new Vector4(r2.x, r2.y, r2.z, 0f));
            return m;
        }

        public Vector3Int editorPreviewOrigin => m_Origin;
        public Vector3Int editorPreviewSize => m_Size;

        internal bool bufferSyncTile
        {
            get => m_BufferSyncTile;
            set => m_BufferSyncTile = value;
        }

        public Vector3 GetCellCenterLocal(Vector3Int position) => CellToLocalInterpolated(position) + CellToLocalInterpolated(m_TileAnchor);

        /// <summary>Where a cell's tile is drawn and its collider shapes sit: the cell's position
        /// plus the tile anchor in cells without the gap (measured: the gap moves cells, not the
        /// anchor within them).</summary>
        internal Vector3 TileCenterLocal(Vector3Int position) =>
            CellToLocalInterpolated(position) + GridMath.CellToLocal(m_TileAnchor, cellLayout, cellSize, Vector3.zero, cellSwizzle);

        public Vector3 GetCellCenterWorld(Vector3Int position) => LocalToWorld(CellToLocalInterpolated(position) + CellToLocalInterpolated(m_TileAnchor));

        // ------------------------------------------------------------------ events

        public static event Action<Tilemap, SyncTile[]> tilemapTileChanged;
        public static event Action<Tilemap, NativeArray<Vector3Int>> tilemapPositionsChanged;
        public static event Action<Tilemap, NativeArray<Vector3Int>> loopEndedForTileAnimation;

        internal static bool HasLoopEndedForTileAnimationCallback() => loopEndedForTileAnimation != null;
        internal static bool HasSyncTileCallback() => tilemapTileChanged != null;
        internal static bool HasPositionsChangedCallback() => tilemapPositionsChanged != null;
        internal static void SetSyncTileCallback(Action<Tilemap, SyncTile[]> callback) => tilemapTileChanged += callback;
        internal static void RemoveSyncTileCallback(Action<Tilemap, SyncTile[]> callback) => tilemapTileChanged -= callback;

        internal void GetLoopEndedForTileAnimationCallbackSettings(ref bool hasEndLoopForTileAnimationCallback) =>
            hasEndLoopForTileAnimationCallback = HasLoopEndedForTileAnimationCallback();

        internal void GetSyncTileCallbackSettings(ref SyncTileCallbackSettings settings)
        {
            settings.hasSyncTileCallback = HasSyncTileCallback();
            settings.hasPositionsChangedCallback = HasPositionsChangedCallback();
            settings.isBufferSyncTile = m_BufferSyncTile;
        }

        internal void SendAndClearSyncTileBuffer() { }

        public partial struct SyncTile
        {
            internal Vector3Int m_Position;
            internal TileBase m_Tile;
            internal TileData m_TileData;

            public Vector3Int position => m_Position;
            public TileBase tile => m_Tile;
            public TileData tileData => m_TileData;
        }

        internal partial struct SyncTileCallbackSettings
        {
            internal bool hasSyncTileCallback;
            internal bool hasPositionsChangedCallback;
            internal bool isBufferSyncTile;
        }

        /// <summary>Tells the callbacks which cells changed (in z, y, x order).</summary>
        void SendChanges(SortedSet<Vector3Int> changed)
        {
            if (changed.Count == 0) return;
            if (tilemapTileChanged != null)
            {
                var tiles = new SyncTile[changed.Count];
                int i = 0;
                foreach (var p in changed)
                {
                    m_Cells.TryGetValue(p, out var cell);
                    tiles[i++] = new SyncTile { m_Position = p, m_Tile = cell?.Tile, m_TileData = DataOf(cell) };
                }
                try
                {
                    tilemapTileChanged(this, tiles);
                }
                catch (Exception e)
                {
                    Debug.LogException(e, this);
                }
            }
            if (tilemapPositionsChanged != null)
            {
                var positions = new Vector3Int[changed.Count];
                changed.CopyTo(positions);
                var array = new NativeArray<Vector3Int>(positions, Allocator.Temp);
                try
                {
                    tilemapPositionsChanged(this, array);
                }
                catch (Exception e)
                {
                    Debug.LogException(e, this);
                }
                finally
                {
                    array.Dispose();
                }
            }
        }

        static TileData DataOf(Cell cell)
        {
            var data = TileData.Default;
            if (cell == null) return data;
            data.sprite = cell.Sprite;
            data.color = cell.Color;
            data.transform = cell.Transform;
            data.gameObject = cell.ObjectToInstantiate;
            data.flags = cell.Flags;
            data.colliderType = cell.Collider;
            return data;
        }

        // ------------------------------------------------------------------ reading tiles

        Cell CellAt(Vector3Int position) => m_Cells.TryGetValue(position, out var cell) ? cell : null;

        internal Object GetTileAsset(Vector3Int position) => CellAt(position)?.Tile;

        public TileBase GetTile(Vector3Int position) => CellAt(position)?.Tile;

        public T GetTile<T>(Vector3Int position) where T : TileBase => GetTile(position) as T;

        public bool HasTile(Vector3Int position) => CellAt(position)?.Tile != null;

        internal Object[] GetTileAssetsBlock(Vector3Int position, Vector3Int blockDimensions)
        {
            var result = new Object[Math.Max(0, blockDimensions.x * blockDimensions.y * blockDimensions.z)];
            int i = 0;
            for (int z = 0; z < blockDimensions.z; z++)
                for (int y = 0; y < blockDimensions.y; y++)
                    for (int x = 0; x < blockDimensions.x; x++)
                        result[i++] = GetTile(position + new Vector3Int(x, y, z));
            return result;
        }

        public TileBase[] GetTilesBlock(BoundsInt bounds)
        {
            var assets = GetTileAssetsBlock(bounds.min, bounds.size);
            var result = new TileBase[assets.Length];
            for (int i = 0; i < assets.Length; i++) result[i] = (TileBase)assets[i];
            return result;
        }

        internal int GetTileAssetsBlockNonAlloc(Vector3Int startPosition, Vector3Int endPosition, Object[] tiles)
        {
            // endPosition is the block's size.
            int count = Math.Max(0, endPosition.x * endPosition.y * endPosition.z);
            int i = 0;
            for (int z = 0; z < endPosition.z; z++)
                for (int y = 0; y < endPosition.y; y++)
                    for (int x = 0; x < endPosition.x; x++)
                    {
                        if (i >= tiles.Length) return i;
                        tiles[i++] = GetTile(startPosition + new Vector3Int(x, y, z));
                    }
            return count;
        }

        public int GetTilesBlockNonAlloc(BoundsInt bounds, TileBase[] tiles) => GetTileAssetsBlockNonAlloc(bounds.min, bounds.size, tiles);

        bool InRange(Vector3Int p, Vector3Int start, Vector3Int end) =>
            p.x >= Math.Min(start.x, end.x) && p.x <= Math.Max(start.x, end.x)
            && p.y >= Math.Min(start.y, end.y) && p.y <= Math.Max(start.y, end.y)
            && p.z >= Math.Min(start.z, end.z) && p.z <= Math.Max(start.z, end.z);

        public int GetTilesRangeCount(Vector3Int startPosition, Vector3Int endPosition)
        {
            int count = 0;
            foreach (var pair in m_Cells)
                if (pair.Value.Tile != null && InRange(pair.Key, startPosition, endPosition)) count++;
            return count;
        }

        internal int GetTileAssetsRangeNonAlloc(Vector3Int startPosition, Vector3Int endPosition, Vector3Int[] positions, Object[] tiles)
        {
            int count = 0;
            foreach (var pair in m_Cells)
            {
                if (pair.Value.Tile == null || !InRange(pair.Key, startPosition, endPosition)) continue;
                if (count >= positions.Length || count >= tiles.Length) break;
                positions[count] = pair.Key;
                tiles[count] = pair.Value.Tile;
                count++;
            }
            return count;
        }

        public int GetTilesRangeNonAlloc(Vector3Int startPosition, Vector3Int endPosition, Vector3Int[] positions, TileBase[] tiles) =>
            GetTileAssetsRangeNonAlloc(startPosition, endPosition, positions, tiles);

        public Sprite GetSprite(Vector3Int position) => CellAt(position)?.Sprite;

        public Matrix4x4 GetTransformMatrix(Vector3Int position) => CellAt(position)?.Transform ?? Matrix4x4.identity;

        public Color GetColor(Vector3Int position) => CellAt(position)?.Color ?? Color.white;

        public TileFlags GetTileFlags(Vector3Int position) => CellAt(position)?.Flags ?? TileFlags.None;

        public GameObject GetInstantiatedObject(Vector3Int position) => CellAt(position)?.Instantiated;

        public GameObject GetObjectToInstantiate(Vector3Int position) => CellAt(position)?.ObjectToInstantiate;

        public Tile.ColliderType GetColliderType(Vector3Int position) => CellAt(position)?.Collider ?? Tile.ColliderType.None;

        internal bool ContainsTileAsset(Object tileAsset)
        {
            foreach (var cell in m_Cells.Values)
                if (cell.Tile != null && cell.Tile == tileAsset) return true;
            return false;
        }

        public bool ContainsTile(TileBase tileAsset) => ContainsTileAsset(tileAsset);

        List<TileBase> UsedTiles()
        {
            var used = new List<TileBase>();
            var seen = new HashSet<TileBase>();
            foreach (var cell in m_Cells.Values)
                if (cell.Tile != null && seen.Add(cell.Tile)) used.Add(cell.Tile);
            return used;
        }

        List<Sprite> UsedSprites()
        {
            var used = new List<Sprite>();
            var seen = new HashSet<Sprite>();
            foreach (var cell in m_Cells.Values)
                if (cell.Sprite != null && seen.Add(cell.Sprite)) used.Add(cell.Sprite);
            return used;
        }

        public int GetUsedTilesCount() => UsedTiles().Count;

        public int GetUsedSpritesCount() => UsedSprites().Count;

        internal int Internal_GetUsedTilesNonAlloc(Object[] usedTiles)
        {
            var used = UsedTiles();
            int n = Math.Min(used.Count, usedTiles.Length);
            for (int i = 0; i < n; i++) usedTiles[i] = used[i];
            return n;
        }

        internal int Internal_GetUsedSpritesNonAlloc(Object[] usedSprites)
        {
            var used = UsedSprites();
            int n = Math.Min(used.Count, usedSprites.Length);
            for (int i = 0; i < n; i++) usedSprites[i] = used[i];
            return n;
        }

        public int GetUsedTilesNonAlloc(TileBase[] usedTiles) => Internal_GetUsedTilesNonAlloc(usedTiles);

        public int GetUsedSpritesNonAlloc(Sprite[] usedSprites) => Internal_GetUsedSpritesNonAlloc(usedSprites);

        // ------------------------------------------------------------------ setting tiles

        internal void SetTileAsset(Vector3Int position, Object tile) => SetTilesInternal(new[] { (position, tile as TileBase) });

        public void SetTile(Vector3Int position, TileBase tile) => SetTilesInternal(new[] { (position, tile) });

        internal void SetTileAssets(Vector3Int[] positionArray, Object[] tileArray)
        {
            if (positionArray == null) throw new ArgumentNullException(nameof(positionArray));
            if (tileArray == null) throw new ArgumentNullException(nameof(tileArray));
            // As Unity's: arrays of other lengths set the pairs both have (measured, 6000.0).
            var changes = new (Vector3Int, TileBase)[Math.Min(positionArray.Length, tileArray.Length)];
            for (int i = 0; i < changes.Length; i++) changes[i] = (positionArray[i], tileArray[i] as TileBase);
            SetTilesInternal(changes);
        }

        public void SetTiles(Vector3Int[] positionArray, TileBase[] tileArray) => SetTileAssets(positionArray, tileArray);

        public void SetTilesBlock(BoundsInt position, TileBase[] tileArray)
        {
            if (tileArray == null) throw new ArgumentNullException(nameof(tileArray));
            var size = position.size;
            int count = size.x * size.y * size.z;
            // As Unity's: a longer array gives its first tiles (games pass ArrayPool rentals), a
            // shorter one sets nothing (measured, 6000.0).
            if (tileArray.Length < count)
            {
                Debug.LogError("Number of tiles passed in does not match block dimensions.");
                return;
            }
            var changes = new List<(Vector3Int, TileBase)>(count);
            int i = 0;
            for (int z = 0; z < size.z; z++)
                for (int y = 0; y < size.y; y++)
                    for (int x = 0; x < size.x; x++)
                        changes.Add((position.min + new Vector3Int(x, y, z), tileArray[i++]));
            SetTilesInternal(changes);
        }

        public void SetTile(TileChangeData tileChangeData, bool ignoreLockFlags) => SetTiles(new[] { tileChangeData }, ignoreLockFlags);

        public void SetTiles(TileChangeData[] tileChangeDataArray, bool ignoreLockFlags)
        {
            if (tileChangeDataArray == null) throw new ArgumentNullException(nameof(tileChangeDataArray));
            var changes = new (Vector3Int, TileBase)[tileChangeDataArray.Length];
            for (int i = 0; i < changes.Length; i++) changes[i] = (tileChangeDataArray[i].position, tileChangeDataArray[i].tile);
            var changed = new SortedSet<Vector3Int>(CellOrder.Instance);
            SetTilesInternal(changes, changed);
            foreach (var change in tileChangeDataArray)
            {
                var cell = CellAt(change.position);
                if (cell == null || cell.Tile == null) continue;
                if (ignoreLockFlags || (cell.Flags & TileFlags.LockColor) == 0)
                {
                    if (cell.Color != change.color) changed.Add(change.position);
                    cell.Color = change.color;
                }
                if (ignoreLockFlags || (cell.Flags & TileFlags.LockTransform) == 0)
                {
                    if (cell.Transform != change.transform)
                    {
                        changed.Add(change.position);
                        ShapeChanged(change.position);
                    }
                    cell.Transform = change.transform;
                }
            }
            m_RenderVersion++;
            SendChanges(changed);
        }

        void Grow(Vector3Int p)
        {
            var min = Vector3Int.Min(m_Origin, p);
            var max = Vector3Int.Max(m_Origin + m_Size, p + Vector3Int.one);
            m_Origin = min;
            m_Size = max - min;
            m_LocalZMin = Math.Min(m_LocalZMin, min.z);
            m_LocalZMax = Math.Max(m_LocalZMax, max.z);
        }

        void ShapeChanged(Vector3Int position)
        {
            m_ShapeVersion++;
            m_ShapeChanges.Add(position);
        }

        /// <summary>Puts tiles on cells and refreshes what changed (see the class comment).
        /// <paramref name="sendTo"/> collects the changed cells instead of sending them.</summary>
        void SetTilesInternal(IReadOnlyList<(Vector3Int Position, TileBase Tile)> changes, SortedSet<Vector3Int> sendTo = null)
        {
            var applied = new List<(Vector3Int Position, TileBase Old, TileBase New)>();
            foreach (var (position, tile) in changes)
            {
                var cell = CellAt(position);
                var old = cell?.Tile;
                if (ReferenceEquals(old, tile) || (old == null && tile == null)) continue;
                if (tile == null)
                {
                    RemoveCell(position, cell);
                }
                else if (cell == null)
                {
                    m_Cells[position] = new Cell { Tile = tile };
                    Grow(position);
                }
                else
                {
                    cell.Tile = tile;
                }
                applied.Add((position, old, tile));
                ShapeChanged(position);
            }
            if (applied.Count == 0) return;
            m_RenderVersion++;
            var refresh = CollectRefreshes(applied);
            var changed = sendTo ?? new SortedSet<Vector3Int>(CellOrder.Instance);
            foreach (var a in applied) changed.Add(a.Position);
            foreach (var p in refresh)
                if (RefreshCell(p)) changed.Add(p);
            if (sendTo == null) SendChanges(changed);
        }

        SortedSet<Vector3Int> CollectRefreshes(List<(Vector3Int Position, TileBase Old, TileBase New)> applied)
        {
            var refresh = new SortedSet<Vector3Int>(CellOrder.Instance);
            var itilemap = Itilemap;
            var previous = itilemap.m_Collect;
            itilemap.m_Collect = refresh;
            try
            {
                foreach (var (position, old, tile) in applied)
                {
                    if (old != null) CallTile(() => old.RefreshTile(position, itilemap));
                    if (tile != null) CallTile(() => tile.RefreshTile(position, itilemap));
                }
            }
            finally
            {
                itilemap.m_Collect = previous;
            }
            return refresh;
        }

        void CallTile(Action call)
        {
            try
            {
                call();
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
            }
        }

        void RemoveCell(Vector3Int position, Cell cell)
        {
            if (cell?.Instantiated != null) Object.DestroyImmediate(cell.Instantiated);
            m_Cells.Remove(position);
        }

        /// <summary>Asks the cell's tile for its data, StartUp and animation; true when what the cell
        /// shows changed.</summary>
        bool RefreshCell(Vector3Int position) => AskData(position, out var data) && ApplyCell(position, data);

        /// <summary>The data the cell's tile gives for it (false for an empty cell).</summary>
        bool AskData(Vector3Int position, out TileData data)
        {
            data = TileData.Default;
            var cell = CellAt(position);
            if (cell == null || cell.Tile == null) return false;
            var result = TileData.Default;
            var itilemap = Itilemap;
            var tile = cell.Tile;
            CallTile(() => tile.GetTileData(position, itilemap, ref result));
            data = result;
            return CellAt(position)?.Tile == tile;
        }

        /// <summary>Takes a tile's data into its cell, then runs the tile's StartUp and reads its
        /// animation; true when what the cell shows changed.</summary>
        bool ApplyCell(Vector3Int position, TileData data)
        {
            var cell = CellAt(position);
            if (cell == null || cell.Tile == null) return false;
            var tile = cell.Tile;
            var itilemap = Itilemap;
            var before = (cell.Sprite, cell.Color, cell.Transform, cell.ObjectToInstantiate);
            cell.Sprite = data.sprite;
            cell.Flags = data.flags;
            if (cell.Collider != data.colliderType) ShapeChanged(position);
            cell.Collider = data.colliderType;
            if (cell.Fresh || (data.flags & TileFlags.LockColor) != 0) cell.Color = data.color;
            if (cell.Fresh || (data.flags & TileFlags.LockTransform) != 0) cell.Transform = data.transform;
            cell.Fresh = false;
            var toInstantiate = data.gameObject;
            if (!ReferenceEquals(cell.ObjectToInstantiate, toInstantiate) || (cell.Instantiated == null && toInstantiate != null))
            {
                if (cell.Instantiated != null) Object.DestroyImmediate(cell.Instantiated);
                cell.Instantiated = null;
                cell.ObjectToInstantiate = toInstantiate;
                if (toInstantiate != null) cell.Instantiated = InstantiateTileObject(position, toInstantiate);
            }
            var instantiated = cell.Instantiated;
            CallTile(() => tile.StartUp(position, itilemap, instantiated));
            var animation = new TileAnimationData();
            bool animated = false;
            CallTile(() => animated = tile.GetTileAnimationData(position, itilemap, ref animation));
            if (animated && animation.animatedSprites != null && animation.animatedSprites.Length > 0)
            {
                bool sameFrames = cell.Frames != null && cell.Frames.Length == animation.animatedSprites.Length;
                cell.Frames = animation.animatedSprites;
                cell.AnimationSpeed = animation.animationSpeed;
                cell.AnimationStartTime = animation.animationStartTime;
                cell.AnimationFlags = animation.flags;
                if (!sameFrames)
                {
                    cell.Frame = -1;
                    cell.AnimationTime = 0f;
                }
            }
            else
            {
                cell.Frames = null;
                cell.Frame = -1;
            }
            if (!ReferenceEquals(before.Sprite, cell.Sprite) || before.Transform != cell.Transform) ShapeChanged(position);
            m_RenderVersion++;
            return !ReferenceEquals(before.Sprite, cell.Sprite) || before.Color != cell.Color || before.Transform != cell.Transform
                || !ReferenceEquals(before.ObjectToInstantiate, cell.ObjectToInstantiate);
        }

        GameObject InstantiateTileObject(Vector3Int position, GameObject original)
        {
            var go = (GameObject)Object.Instantiate(original, transform, false);
            go.transform.localPosition = GetCellCenterLocal(position);
            return go;
        }

        public void RefreshTile(Vector3Int position)
        {
            if (RefreshCell(position))
                SendChanges(new SortedSet<Vector3Int>(CellOrder.Instance) { position });
        }

        internal unsafe void RefreshTilesNative(void* positions, int count)
        {
            var changed = new SortedSet<Vector3Int>(CellOrder.Instance);
            var p = (Vector3Int*)positions;
            var sorted = new SortedSet<Vector3Int>(CellOrder.Instance);
            for (int i = 0; i < count; i++) sorted.Add(p[i]);
            foreach (var position in sorted)
                if (RefreshCell(position)) changed.Add(position);
            SendChanges(changed);
        }

        public void RefreshAllTiles()
        {
            var changed = new SortedSet<Vector3Int>(CellOrder.Instance);
            foreach (var position in new List<Vector3Int>(m_Cells.Keys))
                if (RefreshCell(position)) changed.Add(position);
            SendChanges(changed);
        }

        internal void SwapTileAsset(Object changeTile, Object newTile)
        {
            var from = changeTile as TileBase;
            var to = newTile as TileBase;
            if (from == null) return;
            var applied = new List<(Vector3Int Position, TileBase Old, TileBase New)>();
            foreach (var pair in new List<KeyValuePair<Vector3Int, Cell>>(m_Cells))
            {
                if (pair.Value.Tile != from) continue;
                if (to == null) RemoveCell(pair.Key, pair.Value);
                else pair.Value.Tile = to;
                applied.Add((pair.Key, from, to));
                ShapeChanged(pair.Key);
            }
            if (applied.Count == 0) return;
            m_RenderVersion++;
            var refresh = CollectRefreshes(applied);
            // Swapping asks every refreshed cell for its data first, then runs StartUp and the
            // animation data (measured).
            var changed = new SortedSet<Vector3Int>(CellOrder.Instance);
            foreach (var a in applied) changed.Add(a.Position);
            var datas = new List<(Vector3Int Position, TileData Data)>();
            foreach (var position in refresh)
                if (AskData(position, out var data)) datas.Add((position, data));
            foreach (var (position, data) in datas)
                if (ApplyCell(position, data)) changed.Add(position);
            SendChanges(changed);
        }

        public void SwapTile(TileBase changeTile, TileBase newTile) => SwapTileAsset(changeTile, newTile);

        // ------------------------------------------------------------------ cell data

        void CellChanged(Vector3Int position, bool shape)
        {
            m_RenderVersion++;
            if (shape) ShapeChanged(position);
            SendChanges(new SortedSet<Vector3Int>(CellOrder.Instance) { position });
        }

        public void SetTransformMatrix(Vector3Int position, Matrix4x4 transform)
        {
            var cell = CellAt(position);
            if (cell == null || (cell.Flags & TileFlags.LockTransform) != 0 || cell.Transform == transform) return;
            cell.Transform = transform;
            CellChanged(position, true);
        }

        public void SetColor(Vector3Int position, Color color)
        {
            var cell = CellAt(position);
            if (cell == null || (cell.Flags & TileFlags.LockColor) != 0 || cell.Color == color) return;
            cell.Color = color;
            CellChanged(position, false);
        }

        public void SetTileFlags(Vector3Int position, TileFlags flags)
        {
            var cell = CellAt(position);
            if (cell != null) cell.Flags = flags;
        }

        public void AddTileFlags(Vector3Int position, TileFlags flags)
        {
            var cell = CellAt(position);
            if (cell != null) cell.Flags |= flags;
        }

        public void RemoveTileFlags(Vector3Int position, TileFlags flags)
        {
            var cell = CellAt(position);
            if (cell != null) cell.Flags &= ~flags;
        }

        public void SetColliderType(Vector3Int position, Tile.ColliderType colliderType)
        {
            var cell = CellAt(position);
            if (cell == null || cell.Collider == colliderType) return;
            cell.Collider = colliderType;
            ShapeChanged(position);
        }

        // ------------------------------------------------------------------ animation

        public int GetAnimationFrameCount(Vector3Int position) => CellAt(position)?.Frames?.Length ?? 0;

        public int GetAnimationFrame(Vector3Int position) => CellAt(position) is Cell c && c.Frames != null ? c.Frame : 0;

        public void SetAnimationFrame(Vector3Int position, int frame)
        {
            var cell = CellAt(position);
            if (cell?.Frames == null) return;
            cell.Frame = Mathf.Clamp(frame, 0, cell.Frames.Length - 1);
            cell.AnimationTime = cell.Frame;
            m_RenderVersion++;
        }

        public float GetAnimationTime(Vector3Int position) => CellAt(position) is Cell c && c.Frames != null ? c.AnimationTime : 0f;

        public void SetAnimationTime(Vector3Int position, float time)
        {
            var cell = CellAt(position);
            if (cell?.Frames == null) return;
            cell.AnimationTime = time;
            cell.Frame = Mathf.FloorToInt(time * m_AnimationFrameRate) % cell.Frames.Length;
            m_RenderVersion++;
        }

        public TileAnimationFlags GetTileAnimationFlags(Vector3Int position) => CellAt(position)?.AnimationFlags ?? TileAnimationFlags.None;

        public void SetTileAnimationFlags(Vector3Int position, TileAnimationFlags flags)
        {
            var cell = CellAt(position);
            if (cell != null) cell.AnimationFlags = flags;
        }

        public void AddTileAnimationFlags(Vector3Int position, TileAnimationFlags flags)
        {
            var cell = CellAt(position);
            if (cell != null) cell.AnimationFlags |= flags;
        }

        public void RemoveTileAnimationFlags(Vector3Int position, TileAnimationFlags flags)
        {
            var cell = CellAt(position);
            if (cell != null) cell.AnimationFlags &= ~flags;
        }

        /// <summary>Advances animated cells by a frame's time (in frames of the tile's speed).</summary>
        internal void AdvanceAnimations(float deltaTime, float unscaledDeltaTime)
        {
            List<Vector3Int> ended = null;
            foreach (var pair in m_Cells)
            {
                var cell = pair.Value;
                if (cell.Frames == null || cell.Frames.Length == 0) continue;
                if ((cell.AnimationFlags & TileAnimationFlags.PauseAnimation) != 0)
                {
                    if (cell.Frame < 0) cell.Frame = 0;
                    continue;
                }
                float dt = (cell.AnimationFlags & TileAnimationFlags.UnscaledTime) != 0 ? unscaledDeltaTime : deltaTime;
                if (cell.Frame < 0) cell.AnimationTime = cell.AnimationStartTime * cell.AnimationSpeed * m_AnimationFrameRate;
                else cell.AnimationTime += dt * cell.AnimationSpeed * m_AnimationFrameRate;
                int frame = Mathf.FloorToInt(cell.AnimationTime);
                if ((cell.AnimationFlags & TileAnimationFlags.LoopOnce) != 0 && frame >= cell.Frames.Length)
                {
                    frame = cell.Frames.Length - 1;
                    cell.AnimationFlags |= TileAnimationFlags.PauseAnimation;
                    (ended ??= new List<Vector3Int>()).Add(pair.Key);
                }
                int wrapped = ((frame % cell.Frames.Length) + cell.Frames.Length) % cell.Frames.Length;
                if (wrapped != cell.Frame) m_RenderVersion++;
                cell.Frame = wrapped;
            }
            if (ended != null && loopEndedForTileAnimation != null)
            {
                var array = new NativeArray<Vector3Int>(ended.ToArray(), Allocator.Temp);
                try
                {
                    loopEndedForTileAnimation(this, array);
                }
                catch (Exception e)
                {
                    Debug.LogException(e, this);
                }
                finally
                {
                    array.Dispose();
                }
            }
        }

        /// <summary>The sprite a cell shows now: its animation frame, else its sprite.</summary>
        internal static Sprite ShownSprite(Cell cell) =>
            cell.Frames != null && cell.Frame >= 0 && cell.Frame < cell.Frames.Length ? cell.Frames[cell.Frame] : cell.Sprite;

        // ------------------------------------------------------------------ fills and layout edits

        public void FloodFill(Vector3Int position, TileBase tile) => FloodFillTileAsset(position, tile);

        void FloodFillTileAsset(Vector3Int position, Object tile)
        {
            var target = GetTile(position);
            var fill = tile as TileBase;
            if (ReferenceEquals(target, fill) || (target == null && fill == null)) return;
            var bounds = cellBounds;
            bool limited = target == null;
            var visited = new HashSet<Vector3Int>();
            var queue = new Queue<Vector3Int>();
            var cells = new List<(Vector3Int, TileBase)>();
            queue.Enqueue(position);
            visited.Add(position);
            var steps = new[] { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down };
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                cells.Add((p, fill));
                foreach (var step in steps)
                {
                    var n = p + step;
                    if (visited.Contains(n)) continue;
                    if (limited && !bounds.Contains(n)) continue;
                    if (!ReferenceEquals(GetTile(n), target) && !(target == null && GetTile(n) == null)) continue;
                    visited.Add(n);
                    queue.Enqueue(n);
                }
            }
            SetTilesInternal(cells);
        }

        public void BoxFill(Vector3Int position, TileBase tile, int startX, int startY, int endX, int endY) =>
            BoxFillTileAsset(position, tile, startX, startY, endX, endY);

        /// <summary>Unity's box fill (measured): the bounds grow to include the position, then the
        /// cells connected to it holding its tile are filled, within the box (start to end,
        /// inclusive) and the bounds.</summary>
        void BoxFillTileAsset(Vector3Int position, Object tile, int startX, int startY, int endX, int endY)
        {
            Grow(position);
            var bounds = cellBounds;
            int x0 = Math.Max(startX, bounds.xMin), x1 = Math.Min(endX, bounds.xMax - 1);
            int y0 = Math.Max(startY, bounds.yMin), y1 = Math.Min(endY, bounds.yMax - 1);
            if (position.x < x0 || position.x > x1 || position.y < y0 || position.y > y1) return;
            var target = GetTile(position);
            var fill = tile as TileBase;
            var visited = new HashSet<Vector3Int> { position };
            var queue = new Queue<Vector3Int>();
            queue.Enqueue(position);
            var cells = new List<(Vector3Int, TileBase)>();
            var steps = new[] { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down };
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                cells.Add((p, fill));
                foreach (var step in steps)
                {
                    var n = p + step;
                    if (n.x < x0 || n.x > x1 || n.y < y0 || n.y > y1 || !visited.Add(n)) continue;
                    if (GetTile(n) != target) continue;
                    queue.Enqueue(n);
                }
            }
            SetTilesInternal(cells);
        }

        public void InsertCells(Vector3Int position, Vector3Int insertCells) => InsertCells(position, insertCells.x, insertCells.y, insertCells.z);

        public void InsertCells(Vector3Int position, int numColumns, int numRows, int numLayers) =>
            MoveCells(position, numColumns, numRows, numLayers, insert: true);

        public void DeleteCells(Vector3Int position, Vector3Int deleteCells) => DeleteCells(position, deleteCells.x, deleteCells.y, deleteCells.z);

        public void DeleteCells(Vector3Int position, int numColumns, int numRows, int numLayers) =>
            MoveCells(position, numColumns, numRows, numLayers, insert: false);

        /// <summary>Shifts the cells at or past <paramref name="position"/> on each axis by the
        /// counts (inserting), or removes that many and shifts the rest back (deleting).</summary>
        void MoveCells(Vector3Int position, int columns, int rows, int layers, bool insert)
        {
            var delta = new Vector3Int(columns, rows, layers);
            var moved = new SortedDictionary<Vector3Int, Cell>(CellOrder.Instance);
            foreach (var pair in m_Cells)
            {
                var p = pair.Key;
                var q = p;
                bool removed = false;
                for (int axis = 0; axis < 3; axis++)
                {
                    int d = delta[axis];
                    if (d <= 0 || p[axis] < position[axis]) continue;
                    if (insert) q[axis] += d;
                    else if (p[axis] < position[axis] + d) removed = true;
                    else q[axis] -= d;
                }
                if (removed)
                {
                    if (pair.Value.Instantiated != null) Object.DestroyImmediate(pair.Value.Instantiated);
                    continue;
                }
                moved[q] = pair.Value;
            }
            m_Cells.Clear();
            foreach (var pair in moved) m_Cells[pair.Key] = pair.Value;
            if (insert) m_Size += delta;
            else m_Size = Vector3Int.Max(Vector3Int.zero, m_Size - delta);
            m_RenderVersion++;
            m_ShapeVersion++;
            foreach (var p in m_Cells.Keys) m_ShapeChanges.Add(p);
        }

        public void ClearAllTiles()
        {
            var changed = new SortedSet<Vector3Int>(m_Cells.Keys, CellOrder.Instance);
            foreach (var cell in m_Cells.Values)
                if (cell.Instantiated != null) Object.DestroyImmediate(cell.Instantiated);
            m_Cells.Clear();
            m_Origin = Vector3Int.zero;
            m_Size = new Vector3Int(0, 0, 1);
            m_LocalZMin = 0;
            m_LocalZMax = 1;
            m_RenderVersion++;
            m_ShapeVersion++;
            foreach (var p in changed) m_ShapeChanges.Add(p);
            SendChanges(changed);
        }

        /// <summary>Drops the cells outside the bounds origin and size give.</summary>
        public void ResizeBounds()
        {
            var bounds = cellBounds;
            foreach (var pair in new List<KeyValuePair<Vector3Int, Cell>>(m_Cells))
            {
                if (bounds.Contains(pair.Key)) continue;
                RemoveCell(pair.Key, pair.Value);
                ShapeChanged(pair.Key);
            }
            m_RenderVersion++;
        }

        public void CompressBounds() => CompressTilemapBounds(false);

        internal void CompressBoundsKeepEditorPreview() => CompressTilemapBounds(true);

        void CompressTilemapBounds(bool keepEditorPreview)
        {
            bool first = true;
            Vector3Int min = default, max = default;
            foreach (var pair in m_Cells)
            {
                if (pair.Value.Tile == null) continue;
                var p = pair.Key;
                min = first ? p : Vector3Int.Min(min, p);
                max = first ? p + Vector3Int.one : Vector3Int.Max(max, p + Vector3Int.one);
                first = false;
            }
            if (first)
            {
                m_Origin = Vector3Int.zero;
                m_Size = new Vector3Int(0, 0, 1);
            }
            else
            {
                m_Origin = min;
                m_Size = max - min;
            }
            m_RenderVersion++;
        }

        // ------------------------------------------------------------------ editor preview
        // Editor preview tiles are not kept: the preview layer is the tiles themselves.

        internal Object GetAnyTileAsset(Vector3Int position) => GetTileAsset(position);
        internal TileBase GetAnyTile(Vector3Int position) => GetTile(position);
        internal T GetAnyTile<T>(Vector3Int position) where T : TileBase => GetTile(position) as T;
        internal Object GetEditorPreviewTileAsset(Vector3Int position) => null;
        [Unbound.EditorFaithful("no editor preview tile is ever kept (setting one warns), so this is the empty cell's answer")]
        public TileBase GetEditorPreviewTile(Vector3Int position) => null;
        [Unbound.EditorFaithful("no editor preview tile is ever kept (setting one warns), so this is the empty cell's answer")]
        public T GetEditorPreviewTile<T>(Vector3Int position) where T : TileBase => null;
        internal void SetEditorPreviewTileAsset(Vector3Int position, Object tile) { }
        public void SetEditorPreviewTile(Vector3Int position, TileBase tile) => Stubs.Ignored("Tilemap.SetEditorPreviewTile", "is not honoured yet: editor preview tiles are not kept or drawn");
        [Unbound.EditorFaithful("no editor preview tile is ever kept (setting one warns), so this is the empty cell's answer")]
        public bool HasEditorPreviewTile(Vector3Int position) => false;
        [Unbound.EditorFaithful("no editor preview tile is ever kept (setting one warns), so this is the empty cell's answer")]
        public Sprite GetEditorPreviewSprite(Vector3Int position) => null;
        [Unbound.EditorFaithful("no editor preview tile is ever kept (setting one warns), so this is the empty cell's answer")]
        public Matrix4x4 GetEditorPreviewTransformMatrix(Vector3Int position) => Matrix4x4.identity;
        public void SetEditorPreviewTransformMatrix(Vector3Int position, Matrix4x4 transform) => Stubs.Ignored("Tilemap.SetEditorPreviewTransformMatrix", "is not honoured yet: editor preview tiles are not kept or drawn");
        [Unbound.EditorFaithful("no editor preview tile is ever kept (setting one warns), so this is the empty cell's answer")]
        public Color GetEditorPreviewColor(Vector3Int position) => Color.white;
        public void SetEditorPreviewColor(Vector3Int position, Color color) => Stubs.Ignored("Tilemap.SetEditorPreviewColor", "is not honoured yet: editor preview tiles are not kept or drawn");
        [Unbound.EditorFaithful("no editor preview tile is ever kept (setting one warns), so this is the empty cell's answer")]
        public TileFlags GetEditorPreviewTileFlags(Vector3Int position) => TileFlags.None;
        public void EditorPreviewFloodFill(Vector3Int position, TileBase tile) => Stubs.Ignored("Tilemap.EditorPreviewFloodFill", "is not honoured yet: editor preview tiles are not kept or drawn");
        public void EditorPreviewBoxFill(Vector3Int position, Object tile, int startX, int startY, int endX, int endY) => Stubs.Ignored("Tilemap.EditorPreviewBoxFill", "is not honoured yet: editor preview tiles are not kept or drawn");
        [Unbound.EditorFaithful("no editor preview tile is ever kept (setting one warns), so this is the empty cell's answer")]
        public void ClearAllEditorPreviewTiles() { }

        // ------------------------------------------------------------------ lifecycle and serialization

        internal override void OnEngineEnable() => TilemapSystem.Add(this);

        /// <summary>False for tilemaps loaded or copied with tiles, until their first frame.</summary>
        internal bool m_StartedUp = true;

        /// <summary>The first frame of a loaded tilemap: its cells' objects are instantiated and
        /// their tiles' StartUp runs (before the frame's Start messages, measured).</summary>
        internal void StartUpTiles()
        {
            if (m_StartedUp) return;
            m_StartedUp = true;
            var itilemap = Itilemap;
            foreach (var pair in new List<KeyValuePair<Vector3Int, Cell>>(m_Cells))
            {
                var cell = pair.Value;
                if (cell.Tile == null) continue;
                if (cell.ObjectToInstantiate != null && cell.Instantiated == null)
                    cell.Instantiated = InstantiateTileObject(pair.Key, cell.ObjectToInstantiate);
                var tile = cell.Tile;
                var position = pair.Key;
                var instantiated = cell.Instantiated;
                CallTile(() => tile.StartUp(position, itilemap, instantiated));
            }
        }

        internal override void OnEngineDisable() => TilemapSystem.Remove(this);

        internal override void OnEngineDestroy() => TilemapSystem.Remove(this);

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            T[] Array<T>(string key, Func<YNode, T> read)
            {
                var list = new List<T>();
                foreach (var item in body.Get(key)?.Seq() ?? System.Array.Empty<YNode>()) list.Add(read(item.Get("m_Data")));
                return list.ToArray();
            }
            Object Resolve(YNode node, Type type)
            {
                var r = node?.AsRef() ?? default;
                return r.IsNull ? null : resolver.Resolve(r, type);
            }
            var tiles = Array("m_TileAssetArray", n => Resolve(n, typeof(TileBase)) as TileBase);
            var sprites = Array("m_TileSpriteArray", n => Resolve(n, typeof(Sprite)) as Sprite);
            var matrices = Array("m_TileMatrixArray", ReadMatrix);
            var colors = Array("m_TileColorArray", n => n != null ? new Color(n.Float("r"), n.Float("g"), n.Float("b"), n.Float("a", 1f)) : Color.white);
            var objects = Array("m_TileObjectToInstantiateArray", n => Resolve(n, typeof(GameObject)) as GameObject);
            T At<T>(T[] array, int index) => index >= 0 && index < array.Length && index != 65535 ? array[index] : default;
            m_Cells.Clear();
            foreach (var entry in body.Get("m_Tiles")?.Seq() ?? System.Array.Empty<YNode>())
            {
                var key = entry.Get("first");
                var position = new Vector3Int(key.Int("x"), key.Int("y"), key.Int("z"));
                var data = entry.Get("second");
                var tile = At(tiles, data.Int("m_TileIndex", 65535));
                if (tile == null) continue;
                long allFlags = data.Long("m_AllTileFlags");
                var cell = new Cell
                {
                    Tile = tile,
                    Sprite = At(sprites, data.Int("m_TileSpriteIndex", 65535)),
                    Transform = data.Int("m_TileMatrixIndex", 65535) is int mi && mi >= 0 && mi < matrices.Length ? matrices[mi] : Matrix4x4.identity,
                    Color = data.Int("m_TileColorIndex", 65535) is int ci && ci >= 0 && ci < colors.Length ? colors[ci] : Color.white,
                    ObjectToInstantiate = At(objects, data.Int("m_TileObjectToInstantiateIndex", 65535)),
                    Flags = (TileFlags)(allFlags & 0xff),
                    Collider = (Tile.ColliderType)((allFlags >> 30) & 3),
                    Fresh = false,
                };
                m_Cells[position] = cell;
            }
            foreach (var entry in body.Get("m_AnimatedTiles")?.Seq() ?? System.Array.Empty<YNode>())
            {
                var key = entry.Get("first");
                var position = new Vector3Int(key.Int("x"), key.Int("y"), key.Int("z"));
                if (!m_Cells.TryGetValue(position, out var cell)) continue;
                var data = entry.Get("second");
                var frames = new List<Sprite>();
                foreach (var s in data.Get("m_AnimatedSprites")?.Seq() ?? System.Array.Empty<YNode>())
                    frames.Add(Resolve(s, typeof(Sprite)) as Sprite);
                cell.Frames = frames.ToArray();
                cell.AnimationSpeed = data.Float("m_AnimationSpeed", 1f);
                cell.AnimationStartTime = data.Float("m_AnimationTimeOffset");
                cell.AnimationFlags = (TileAnimationFlags)data.Int("m_Flags");
            }
            m_StartedUp = false;
            m_AnimationFrameRate = body.Float("m_AnimationFrameRate", 1f);
            m_Color = body.Color("m_Color", Color.white);
            var origin = body.Get("m_Origin");
            if (origin != null) m_Origin = new Vector3Int(origin.Int("x"), origin.Int("y"), origin.Int("z"));
            var size = body.Get("m_Size");
            if (size != null) m_Size = new Vector3Int(size.Int("x"), size.Int("y"), size.Int("z"));
            m_LocalZMin = m_Origin.z;
            m_LocalZMax = m_Origin.z + m_Size.z;
            m_TileAnchor = body.Vector3("m_TileAnchor", new Vector3(0.5f, 0.5f, 0f));
            m_Orientation = (Orientation)body.Int("m_TileOrientation");
            var orientationMatrix = body.Get("m_TileOrientationMatrix");
            m_OrientationMatrix = orientationMatrix != null ? ReadMatrix(orientationMatrix) : OrientationMatrixOf(m_Orientation);
            m_RenderVersion++;
            m_ShapeVersion++;
        }

        internal static Matrix4x4 ReadMatrix(YNode n)
        {
            if (n == null) return Matrix4x4.identity;
            var m = new Matrix4x4();
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    m[r, c] = n.Float("e" + r + c, r == c ? 1f : 0f);
            return m;
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (Tilemap)source;
            m_Cells.Clear();
            foreach (var pair in s.m_Cells)
            {
                var copy = pair.Value.Copy();
                copy.Instantiated = null;
                m_Cells[pair.Key] = copy;
            }
            m_Origin = s.m_Origin;
            m_Size = s.m_Size;
            m_LocalZMin = s.m_LocalZMin;
            m_LocalZMax = s.m_LocalZMax;
            m_Color = s.m_Color;
            m_TileAnchor = s.m_TileAnchor;
            m_Orientation = s.m_Orientation;
            m_OrientationMatrix = s.m_OrientationMatrix;
            m_AnimationFrameRate = s.m_AnimationFrameRate;
            m_StartedUp = false;
            m_RenderVersion++;
            m_ShapeVersion++;
        }

        public enum Orientation
        {
            XY = 0,
            XZ = 1,
            YX = 2,
            YZ = 3,
            ZX = 4,
            ZY = 5,
            Custom = 6,
        }
    }
}

namespace Unbound.Runtime
{
    using UnityEngine;
    using UnityEngine.Tilemaps;
    using PL = UnityEngine.PlayerLoop;

    /// <summary>The enabled tilemaps: their tile animations advance and their colliders take
    /// the frame's tile changes before rendering.</summary>
    internal static class TilemapSystem
    {
        static readonly List<Tilemap> s_Tilemaps = new List<Tilemap>();
        static bool s_Registered;

        internal static void Add(Tilemap tilemap)
        {
            if (!s_Registered)
            {
                s_Registered = true;
                PlayerLoopDriver.Register(typeof(PL.Initialization.SynchronizeState), StartPending);
                PlayerLoopDriver.Register(typeof(PL.PostLateUpdate.UpdateAllRenderers), Update);
            }
            if (!s_Tilemaps.Contains(tilemap)) s_Tilemaps.Add(tilemap);
            if (!tilemap.m_StartedUp) s_Pending.Add(tilemap);
        }

        static readonly List<Tilemap> s_Pending = new List<Tilemap>();

        static void StartPending()
        {
            if (s_Pending.Count == 0) return;
            var pending = s_Pending.ToArray();
            s_Pending.Clear();
            foreach (var tilemap in pending)
                if (tilemap != null && tilemap.isActiveAndEnabled) tilemap.StartUpTiles();
        }

        internal static void Remove(Tilemap tilemap) => s_Tilemaps.Remove(tilemap);

        static void Update()
        {
            foreach (var tilemap in s_Tilemaps.ToArray())
            {
                if (tilemap == null) continue;
                tilemap.AdvanceAnimations(Time.deltaTime, Time.unscaledDeltaTime);
                var collider = tilemap.GetComponent<TilemapCollider2D>();
                if (collider != null && collider.hasTilemapChanges) collider.ProcessTilemapChanges();
            }
        }
    }
}
