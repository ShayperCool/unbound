/*
Copyright (c) 2009-2010 Mikko Mononen memon@inside.org
recast4j copyright (c) 2015-2019 Piotr Piastucki piotr@jtilia.org
DotRecast Copyright (c) 2023-2024 Choi Ikpil ikpil@naver.com

This software is provided 'as-is', without any express or implied
warranty.  In no event will the authors be held liable for any damages
arising from the use of this software.
Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:
1. The origin of this software must not be misrepresented; you must not
 claim that you wrote the original software. If you use this software
 in a product, an acknowledgment in the product documentation would be
 appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be
 misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.
*/

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DotRecast.Detour.Crowd
{
    // Spatial hash mirroring recastnavigation's dtProximityGrid: a flat item
    // pool chained from power-of-two hash buckets. Clearing resets the pool
    // head and bucket heads, so a steady-state update allocates nothing.
    internal class DtProximityGrid
    {
        private struct Item
        {
            public int id;
            public int x;
            public int y;
            public int next;
        }

        // Unbound: cells may differ along x and y, and change from update to update (Unity's crowd
        // sizes them to its agents, see DtCrowd.BuildProximityGrid).
        private float _cellSizeX, _cellSizeY;
        private float _invCellSizeX, _invCellSizeY;

        private int[] _buckets; // index of the first pool item per bucket, -1 if empty
        private Item[] _pool;
        private int _poolHead;

        // Wide rectangles use a pooled bounds tree rather than one entry per covered cell.
        // The query still orders candidates by their first overlapping cell and insertion order.
        const int MaxCellsPerItem = 64;
        struct BoundsItem { public int id, order, minx, miny, maxx, maxy; }
        struct BoundsNode { public int minx, miny, maxx, maxy, start, count, left, right, maxOrder; }
        BoundsItem[] _bounds = new BoundsItem[256];
        BoundsNode[] _nodes = new BoundsNode[512];
        int _boundsCount, _nodeCount;
        bool _useBounds, _treeBuilt;
        static int CompareBounds(BoundsItem a, BoundsItem b, bool x) => x
            ? ((long)a.minx + a.maxx).CompareTo((long)b.minx + b.maxx)
            : ((long)a.miny + a.maxy).CompareTo((long)b.miny + b.maxy);

        // In-place heapsort avoids comparer delegates and temporary arrays during tree rebuilds.
        void SortBounds(int start, int count, bool x)
        {
            for (int root = count / 2 - 1; root >= 0; root--) SiftBounds(start, root, count, x);
            for (int end = count - 1; end > 0; end--)
            {
                (_bounds[start], _bounds[start + end]) = (_bounds[start + end], _bounds[start]);
                SiftBounds(start, 0, end, x);
            }
        }

        void SiftBounds(int start, int root, int count, bool x)
        {
            while (root < count / 2)
            {
                int child = root * 2 + 1;
                if (child + 1 < count && CompareBounds(_bounds[start + child], _bounds[start + child + 1], x) < 0) child++;
                if (CompareBounds(_bounds[start + root], _bounds[start + child], x) >= 0) return;
                (_bounds[start + root], _bounds[start + child]) = (_bounds[start + child], _bounds[start + root]);
                root = child;
            }
        }
        internal int StoredItemCount => _useBounds ? _boundsCount : _poolHead;

        static int Cell(float value, float inverse)
        {
            double cell = MathF.Floor(value * inverse);
            return cell <= int.MinValue ? int.MinValue : cell >= int.MaxValue ? int.MaxValue : (int)cell;
        }

        static ulong CellKey(int x, int y) => ((ulong)((uint)y ^ 0x80000000u) << 32) | ((uint)x ^ 0x80000000u);

        int BuildTree(int start, int count)
        {
            int index = _nodeCount++;
            var node = new BoundsNode { minx = int.MaxValue, miny = int.MaxValue,
                maxx = int.MinValue, maxy = int.MinValue, start = start, count = count, left = -1 };
            for (int i = start; i < start + count; i++)
            {
                node.maxOrder = Math.Max(node.maxOrder, _bounds[i].order);
                node.minx = Math.Min(node.minx, _bounds[i].minx); node.miny = Math.Min(node.miny, _bounds[i].miny);
                node.maxx = Math.Max(node.maxx, _bounds[i].maxx); node.maxy = Math.Max(node.maxy, _bounds[i].maxy);
            }
            if (count > 8)
            {
                SortBounds(start, count, (long)node.maxx - node.minx >= (long)node.maxy - node.miny);
                int half = count / 2;
                node.left = BuildTree(start, half); node.right = BuildTree(start + half, count - half);
            }
            _nodes[index] = node;
            return index;
        }

        void QueryTree(int index, int minx, int miny, int maxx, int maxy,
            Span<int> ids, Span<int> orders, Span<ulong> keys, ref int count, int capacity)
        {
            ref var node = ref _nodes[index];
            if (node.minx > maxx || node.miny > maxy || node.maxx < minx || node.maxy < miny) return;
            ulong first = CellKey(Math.Max(minx, node.minx), Math.Max(miny, node.miny));
            if (count == capacity && (first > keys[count - 1]
                || (first == keys[count - 1] && node.maxOrder <= orders[count - 1]))) return;
            if (node.left >= 0)
            {
                int left = node.left, right = node.right;
                ulong leftKey = CellKey(Math.Max(minx, _nodes[left].minx), Math.Max(miny, _nodes[left].miny));
                ulong rightKey = CellKey(Math.Max(minx, _nodes[right].minx), Math.Max(miny, _nodes[right].miny));
                if (rightKey < leftKey || (rightKey == leftKey && _nodes[right].maxOrder > _nodes[left].maxOrder))
                    (left, right) = (right, left);
                QueryTree(left, minx, miny, maxx, maxy, ids, orders, keys, ref count, capacity);
                QueryTree(right, minx, miny, maxx, maxy, ids, orders, keys, ref count, capacity);
                return;
            }
            for (int i = node.start; i < node.start + node.count; i++)
            {
                ref var item = ref _bounds[i];
                if (item.minx > maxx || item.miny > maxy || item.maxx < minx || item.maxy < miny) continue;
                ulong key = CellKey(Math.Max(minx, item.minx), Math.Max(miny, item.miny));
                int existing = 0;
                while (existing < count && ids[existing] != item.id) existing++;
                if (existing < count)
                {
                    if (keys[existing] < key || (keys[existing] == key && orders[existing] >= item.order)) continue;
                    for (int j = existing; j + 1 < count; j++) { ids[j] = ids[j + 1]; orders[j] = orders[j + 1]; keys[j] = keys[j + 1]; }
                    count--;
                }
                int at = 0;
                while (at < count && (keys[at] < key || (keys[at] == key && orders[at] > item.order))) at++;
                if (at >= capacity) continue;
                int last = Math.Min(count, capacity - 1);
                for (int j = last; j > at; j--) { ids[j] = ids[j - 1]; orders[j] = orders[j - 1]; keys[j] = keys[j - 1]; }
                ids[at] = item.id; orders[at] = item.order; keys[at] = key;
                count = Math.Min(count + 1, capacity);
            }
        }

        public DtProximityGrid(float cellSize)
        {
            SetCellSize(cellSize, cellSize);
            _pool = new Item[256];
            _buckets = new int[NextPow2(_pool.Length)];
            Array.Fill(_buckets, -1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long CombineKey(int x, int y)
        {
            uint ux = (uint)x;
            uint uy = (uint)y;
            return ((long)ux << 32) | uy;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DecomposeKey(long key, out int x, out int y)
        {
            uint ux = (uint)(key >> 32);
            uint uy = (uint)key;
            x = (int)ux;
            y = (int)uy;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int HashPos2(int x, int y, int n)
        {
            return ((x * 73856093) ^ (y * 19349663)) & (n - 1);
        }

        private static int NextPow2(int v)
        {
            v--;
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            v++;
            return v;
        }

        /// Unbound: the cells' sizes along x and y (takes effect for the items added after).
        public void SetCellSize(float x, float y)
        {
            _cellSizeX = x;
            _cellSizeY = y;
            _invCellSizeX = 1.0f / x;
            _invCellSizeY = 1.0f / y;
        }

        public void Clear()
        {
            Array.Fill(_buckets, -1);
            _poolHead = 0;
            _boundsCount = _nodeCount = 0;
            _useBounds = _treeBuilt = false;
        }

        // Unlike upstream, the pool grows instead of dropping items: DtCrowd
        // does not cap the agent count, so the grid cannot size itself up front.
        private void Grow()
        {
            Array.Resize(ref _pool, _pool.Length * 2);
            _buckets = new int[NextPow2(_pool.Length)];
            Array.Fill(_buckets, -1);
            for (int i = 0; i < _poolHead; ++i)
            {
                ref Item item = ref _pool[i];
                int h = HashPos2(item.x, item.y, _buckets.Length);
                item.next = _buckets[h];
                _buckets[h] = i;
            }
        }

        public void AddItem(DtCrowdAgent agent, float minx, float miny, float maxx, float maxy)
        {
            int iminx = Cell(minx, _invCellSizeX), iminy = Cell(miny, _invCellSizeY);
            int imaxx = Cell(maxx, _invCellSizeX), imaxy = Cell(maxy, _invCellSizeY);
            if (iminx > imaxx || iminy > imaxy) return;
            if (_boundsCount == _bounds.Length)
            {
                Array.Resize(ref _bounds, _bounds.Length * 2);
                Array.Resize(ref _nodes, _bounds.Length * 2);
            }
            _bounds[_boundsCount] = new BoundsItem { id = agent.idx, order = _boundsCount,
                minx = iminx, miny = iminy, maxx = imaxx, maxy = imaxy };
            _boundsCount++;
            _treeBuilt = false;
            if (((double)imaxx - iminx + 1) * ((double)imaxy - iminy + 1) > MaxCellsPerItem) _useBounds = true;
            if (_useBounds) return;

            for (int y = iminy; ; ++y)
            {
                for (int x = iminx; ; ++x)
                {
                    if (_poolHead >= _pool.Length)
                    {
                        Grow();
                    }

                    int h = HashPos2(x, y, _buckets.Length);
                    int idx = _poolHead;
                    _poolHead++;

                    ref Item item = ref _pool[idx];
                    item.x = x;
                    item.y = y;
                    item.id = agent.idx;
                    item.next = _buckets[h];
                    _buckets[h] = idx;
                    if (x == imaxx) break;
                }
                if (y == imaxy) break;
            }
        }

        public int QueryItems(float minx, float miny, float maxx, float maxy, Span<int> ids, int maxIds)
        {
            int iminx = Cell(minx, _invCellSizeX), iminy = Cell(miny, _invCellSizeY);
            int imaxx = Cell(maxx, _invCellSizeX), imaxy = Cell(maxy, _invCellSizeY);
            if (maxIds <= 0 || iminx > imaxx || iminy > imaxy) return 0;
            if (_useBounds || ((double)imaxx - iminx + 1) * ((double)imaxy - iminy + 1) > MaxCellsPerItem)
            {
                if (_boundsCount == 0) return 0;
                if (!_treeBuilt) { _nodeCount = 0; BuildTree(0, _boundsCount); _treeBuilt = true; }
                int count = 0;
                Span<int> orders = stackalloc int[maxIds];
                Span<ulong> keys = stackalloc ulong[maxIds];
                QueryTree(0, iminx, iminy, imaxx, imaxy, ids, orders, keys, ref count, maxIds);
                return count;
            }

            int n = 0;

            for (int y = iminy; ; ++y)
            {
                for (int x = iminx; ; ++x)
                {
                    int idx = _buckets[HashPos2(x, y, _buckets.Length)];
                    while (idx != -1)
                    {
                        ref Item item = ref _pool[idx];
                        if (item.x == x && item.y == y)
                        {
                            // Check if the id exists already.
                            int end = n;
                            int i = 0;
                            while (i != end && ids[i] != item.id)
                            {
                                ++i;
                            }

                            // Item not found, add it.
                            if (i == n)
                            {
                                ids[n++] = item.id;

                                if (n >= maxIds)
                                    return n;
                            }
                        }

                        idx = item.next;
                    }
                    if (x == imaxx) break;
                }
                if (y == imaxy) break;
            }

            return n;
        }

        public IEnumerable<(long, int)> GetItemCounts()
        {
            // Debug/visualization only - allocation here is fine. Wide bounds are represented
            // by their first cell, one storage entry each rather than expanded cell coverage.
            if (_useBounds)
            {
                for (int i = 0; i < _boundsCount; i++) yield return (CombineKey(_bounds[i].minx, _bounds[i].miny), 1);
                yield break;
            }
            Dictionary<long, int> counts = new Dictionary<long, int>();
            for (int i = 0; i < _poolHead; ++i)
            {
                long key = CombineKey(_pool[i].x, _pool[i].y);
                counts.TryGetValue(key, out var count);
                counts[key] = count + 1;
            }

            foreach (var e in counts)
            {
                yield return (e.Key, e.Value);
            }
        }

        public float GetCellSizeX() => _cellSizeX;

        public float GetCellSizeY() => _cellSizeY;
    }
}
