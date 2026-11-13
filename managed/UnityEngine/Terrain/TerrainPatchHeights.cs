using System;

namespace UnityEngine
{
    public sealed partial class TerrainData
    {
        /// <summary>
        /// The heightmap's quadtree of patches of 16 × 16 cells, level 0 the finest: per node, row
        /// by row, its largest height error (the largest distance of the samples it covers from the
        /// bilinear surface through its vertices, every 2^level samples) and its lowest and highest
        /// raw heights. The levels of detail are worked out from it; GetMaximumHeightError and
        /// GetPatchMinMaxHeights give it level by level from the finest, in heights of 0 to 1
        /// (matches the Unity 6000.0.71f1 Editor, tests/projects/terrainlod).
        /// </summary>
        internal sealed class PatchHeights
        {
            internal const int Cells = 16;
            internal int Levels;
            internal float[][] Error;
            internal int[][] Low, High;

            internal int Side(int level) => 1 << (Levels - level);
        }

        PatchHeights m_PatchHeights;
        int m_PatchHeightsVersion = -1;

        /// <summary>The quadtree levels above the finest patches of a heightmap of that many samples
        /// a side.</summary>
        internal static int PatchLevels(int resolution)
        {
            int levels = 0;
            while ((PatchHeights.Cells << levels) < resolution - 1) levels++;
            return levels;
        }

        /// <summary>The patches of the current heights; null when the heightmap is not a power of
        /// two plus one samples a side, from 17 on.</summary>
        internal PatchHeights Patches()
        {
            if (m_PatchHeightsVersion == m_HeightsVersion) return m_PatchHeights;
            m_PatchHeightsVersion = m_HeightsVersion;
            int cells = m_Resolution - 1;
            if (cells < PatchHeights.Cells || (cells & (cells - 1)) != 0) return m_PatchHeights = null;
            int levels = PatchLevels(m_Resolution);
            var patches = new PatchHeights
            {
                Levels = levels,
                Error = new float[levels + 1][],
                Low = new int[levels + 1][],
                High = new int[levels + 1][],
            };
            for (int level = 0; level <= levels; level++)
            {
                int side = patches.Side(level), count = side * side, span = PatchHeights.Cells << level;
                var error = patches.Error[level] = new float[count];
                var lowest = patches.Low[level] = new int[count];
                var highest = patches.High[level] = new int[count];
                for (int z = 0; z < side; z++)
                    for (int x = 0; x < side; x++)
                    {
                        int i = z * side + x;
                        error[i] = MaximumHeightError(x * span, z * span, level);
                        int low = int.MaxValue, high = int.MinValue;
                        if (level == 0)
                        {
                            for (int sz = z * span; sz <= (z + 1) * span; sz++)
                                for (int sx = x * span; sx <= (x + 1) * span; sx++)
                                {
                                    int h = RawHeight(sx, sz);
                                    low = Math.Min(low, h);
                                    high = Math.Max(high, h);
                                }
                        }
                        else
                        {
                            // A node's heights are its four children's.
                            int childSide = patches.Side(level - 1);
                            for (int c = 0; c < 4; c++)
                            {
                                int child = (z * 2 + (c >> 1)) * childSide + x * 2 + (c & 1);
                                low = Math.Min(low, patches.Low[level - 1][child]);
                                high = Math.Max(high, patches.High[level - 1][child]);
                            }
                        }
                        lowest[i] = low;
                        highest[i] = high;
                    }
            }
            return m_PatchHeights = patches;
        }

        /// <summary>A node's largest height error in raw heights.</summary>
        float MaximumHeightError(int x0, int z0, int level)
        {
            if (level == 0) return 0f;
            int step = 1 << level;
            float inverse = 1f / step, worst = 0f;
            for (int cz = 0; cz < PatchHeights.Cells; cz++)
                for (int cx = 0; cx < PatchHeights.Cells; cx++)
                {
                    int ax = x0 + cx * step, az = z0 + cz * step;
                    float h00 = RawHeight(ax, az), h10 = RawHeight(ax + step, az);
                    float h01 = RawHeight(ax, az + step), h11 = RawHeight(ax + step, az + step);
                    float dz = (h01 - h00) * inverse, dx = (h10 - h00) * inverse;
                    float dxz = (h11 - h10 - (h01 - h00)) * inverse * inverse;
                    for (int v = 0; v <= step; v++)
                        for (int u = 0; u <= step; u++)
                        {
                            float error = Math.Abs(u * v * dxz + u * dx + h00 + v * dz - RawHeight(ax + u, az + v));
                            if (error > worst) worst = error;
                        }
                }
            return worst;
        }

        public float[] GetMaximumHeightError()
        {
            var patches = Patches();
            if (patches == null) return Array.Empty<float>();
            var result = new float[PatchCount(patches)];
            int n = 0;
            for (int level = 0; level <= patches.Levels; level++)
                foreach (var error in patches.Error[level]) result[n++] = error / MaxHeight;
            return result;
        }

        public PatchExtents[] GetPatchMinMaxHeights()
        {
            var patches = Patches();
            if (patches == null) return Array.Empty<PatchExtents>();
            var result = new PatchExtents[PatchCount(patches)];
            int n = 0;
            for (int level = 0; level <= patches.Levels; level++)
                for (int i = 0; i < patches.Low[level].Length; i++)
                    result[n++] = new PatchExtents { min = patches.Low[level][i] / (float)MaxHeight, max = patches.High[level][i] / (float)MaxHeight };
            return result;
        }

        static int PatchCount(PatchHeights patches)
        {
            int count = 0;
            for (int level = 0; level <= patches.Levels; level++) count += patches.Error[level].Length;
            return count;
        }
    }
}
