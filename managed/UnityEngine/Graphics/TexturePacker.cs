using System;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;

namespace Unbound.Runtime
{
    /// <summary>Texture2D.PackTextures and the terrain detail atlas: the textures' places in an
    /// atlas, as the Unity 6000.0.71f1 Editor lays them out (tests/projects/grass `pack` rows and
    /// its detail atlases).
    ///
    /// The atlas starts as the next power of two of the larger of the widest texture and the
    /// square root of the textures' total area (truncated), and as high as the next power of two of
    /// the larger of the highest texture and that area over the width, both at most the maximum
    /// size (itself rounded up to a power of two, at least 8; a side over it is halved first). The
    /// textures go in one after another into a binary tree of free rectangles, sorted first by area,
    /// then by width (then height), then by height (then width), all larger first: a rectangle
    /// takes a texture when it fits within 1e-5 or a texture plus its padding leaves it; else it is
    /// split across its longer leftover, the texture's side (plus padding) first. Padding is not
    /// needed against the atlas's right and top edges. When no order fits, the narrower side
    /// doubles (the height first on a tie, the other side once one is at the maximum); at the
    /// maximum in both, every texture over 4 texels in both sides shrinks to 3/4 and the packing
    /// starts again. Unmeasured: the order of textures of equal sort keys among more than 16
    /// textures, and the pixels of shrunk textures (scaled bilinearly here).</summary>
    internal static class TexturePacker
    {
        /// <summary>A compressed block's side in texels (the formats the atlas keeps compressed).</summary>
        const int BlockSide = 4;

        static float Aligned(float side, bool blockAligned) => blockAligned ? (float)(((int)side + BlockSide - 1) & ~(BlockSide - 1)) : side;

        /// <summary>How close a free rectangle must be to a texture's size to take it as it is.</summary>
        const float Epsilon = 1e-5f;
        /// <summary>Rounds of shrinking at the maximum size before packing gives up (PackTextures
        /// then returns null).</summary>
        const int MaxShrinks = 100;

        /// <summary>Edges of the atlas a free rectangle touches: there it needs no padding.</summary>
        const int LeftEdge = 1, RightEdge = 2, BottomEdge = 4, TopEdge = 8;

        sealed class Node
        {
            internal Node First, Second;
            internal float X, Y, Width, Height;
            internal bool Used;
            internal int Edges;
        }

        /// <summary>Lays out textures of the given sizes. <paramref name="widths"/> and
        /// <paramref name="heights"/> may shrink (to fit the maximum size); <paramref name="x"/> and
        /// <paramref name="y"/> receive each texture's corner in texels. False when the textures
        /// do not fit even after shrinking (textures of 4 texels or less in a side never shrink).</summary>
        internal static bool Layout(int[] widths, int[] heights, int padding, int maximumSize, int[] x, int[] y, out int atlasWidth, out int atlasHeight,
            bool blockAligned = false)
        {
            int count = widths.Length;
            // A maximum over the largest texture side (int.MaxValue meaning "no limit") is that side
            // (the cap is not measured).
            int max = NextPowerOfTwo(Math.Clamp(maximumSize, 8, UnityEngine.SystemInfo.maxTextureSize));
            for (int i = 0; i < count; i++)
            {
                widths[i] = FitSide(widths[i], max);
                heights[i] = FitSide(heights[i], max);
            }
            // A 32-bit unsigned sum: it wraps past 2^32 texels (unmeasured).
            uint area = 0;
            int widest = 0, highest = 0;
            for (int i = 0; i < count; i++)
            {
                area += (uint)widths[i] * (uint)heights[i];
                widest = Math.Max(widest, widths[i]);
                highest = Math.Max(highest, heights[i]);
            }
            int width = Math.Min(max, NextPowerOfTwo(Math.Max(widest, (int)MathF.Sqrt(area))));
            int height = Math.Min(max, NextPowerOfTwo((int)Math.Max((uint)highest, area / (uint)Math.Max(1, width))));
            var order = new int[count];
            var placed = new Node[count];
            for (int shrinks = 0; shrinks < MaxShrinks;)
            {
                for (int mode = 0; mode < 3; mode++)
                {
                    for (int i = 0; i < count; i++) order[i] = i;
                    Sort(order, widths, heights, mode);
                    var root = new Node { Width = width, Height = height, Edges = LeftEdge | RightEdge | BottomEdge | TopEdge };
                    bool fits = true;
                    foreach (int i in order)
                    {
                        placed[i] = Insert(root, padding, padding, widths[i], heights[i], blockAligned);
                        if (placed[i] == null) { fits = false; break; }
                    }
                    if (!fits) continue;
                    for (int i = 0; i < count; i++) { x[i] = (int)placed[i].X; y[i] = (int)placed[i].Y; }
                    atlasWidth = width;
                    atlasHeight = height;
                    return true;
                }
                if (width == max && height == max)
                {
                    for (int i = 0; i < count; i++)
                        if (widths[i] > 4 && heights[i] > 4)
                        {
                            widths[i] = widths[i] * 3 / 4;
                            heights[i] = heights[i] * 3 / 4;
                        }
                    shrinks++;
                }
                else if (width == max) height *= 2;
                else if (height == max) width *= 2;
                else if (width < height) width *= 2;
                else height *= 2;
            }
            atlasWidth = atlasHeight = 0;
            return false;
        }

        /// <summary>A side over the maximum halves until it fits (a side of 9 or less halves once more at most).</summary>
        static int FitSide(int side, int max)
        {
            if (side <= max || side <= 4) return side;
            int previous;
            do
            {
                previous = side;
                side >>= 1;
            } while (side > max && previous > 9);
            return side;
        }

        static int NextPowerOfTwo(int v)
        {
            uint u = (uint)v - 1;
            u |= u >> 1; u |= u >> 2; u |= u >> 4; u |= u >> 8; u |= u >> 16;
            return (int)(u + 1);
        }

        /// <summary>Larger first: by area (mode 0), by width then height (1), by height then width (2);
        /// equal keys keep their order.</summary>
        static void Sort(int[] order, int[] widths, int[] heights, int mode)
        {
            bool Before(int a, int b) => mode switch
            {
                0 => widths[a] * heights[a] > widths[b] * heights[b],
                1 => widths[a] != widths[b] ? widths[a] > widths[b] : heights[a] > heights[b],
                _ => heights[a] != heights[b] ? heights[a] > heights[b] : widths[a] > widths[b],
            };
            for (int i = 1; i < order.Length; i++)
            {
                int item = order[i], j = i - 1;
                for (; j >= 0 && Before(item, order[j]); j--) order[j + 1] = order[j];
                order[j + 1] = item;
            }
        }

        /// <summary>A free rectangle for a texture. <paramref name="blockAligned"/> (a block-compressed
        /// atlas): rectangles split at whole blocks (4 texels), and one with at most a block left over
        /// in both sides takes the texture as it is.</summary>
        static Node Insert(Node node, float padX, float padY, float width, float height, bool blockAligned)
        {
            while (true)
            {
                if (node.First != null)
                {
                    var found = Insert(node.First, padX, padY, width, height, blockAligned);
                    if (found != null) return found;
                    node = node.Second;
                    continue;
                }
                if (node.Used) return null;
                float freeWidth = node.Width, freeHeight = node.Height;
                float roomX = freeWidth - padX + 0.5f, roomY = freeHeight - padY + 0.5f;
                float leftX = freeWidth - width, leftY = freeHeight - height;
                if (roomX < width || roomY < height)
                {
                    bool refused = false;
                    if (width > roomX)
                    {
                        // At the atlas's right edge the padding is what is left.
                        refused = freeWidth < width || (node.Edges & RightEdge) == 0;
                        if (!refused) padX = leftX;
                    }
                    if (roomY < height)
                    {
                        if (freeHeight < height || (node.Edges & TopEdge) == 0) return null;
                        padY = leftY;
                    }
                    if (refused) return null;
                }
                if (leftY <= Math.Max(Epsilon, padY + padY) && leftX <= Math.Max(Epsilon, padX + padX))
                {
                    node.Used = true;
                    return node;
                }
                if (blockAligned && leftX <= BlockSide && leftY <= BlockSide)
                {
                    node.Used = true;
                    return node;
                }
                float x = node.X, y = node.Y;
                Node first, second;
                if (leftX <= leftY)
                {
                    float split = Aligned(height + padY, blockAligned);
                    first = new Node { X = x, Y = y, Width = (freeWidth + x) - x, Height = (split + y) - y };
                    if (Math.Abs(first.Width - freeWidth) < Epsilon && Math.Abs(first.Height - freeHeight) < Epsilon)
                    {
                        node.Used = true;
                        return node;
                    }
                    second = new Node { X = x, Y = split + y, Width = (freeWidth + x) - x, Height = (freeHeight + y) - (split + y) };
                    first.Edges = node.Edges & (LeftEdge | RightEdge | BottomEdge);
                    second.Edges = node.Edges & (LeftEdge | RightEdge | TopEdge);
                }
                else
                {
                    float split = Aligned(width + padX, blockAligned);
                    first = new Node { X = x, Y = y, Width = (split + x) - x, Height = (freeHeight + y) - y };
                    if (Math.Abs(first.Width - freeWidth) < Epsilon && Math.Abs(first.Height - freeHeight) < Epsilon)
                    {
                        node.Used = true;
                        return node;
                    }
                    second = new Node { X = x + split, Y = y, Width = (freeWidth + x) - (x + split), Height = (freeHeight + y) - y };
                    first.Edges = node.Edges & (BottomEdge | TopEdge | LeftEdge);
                    second.Edges = node.Edges & (BottomEdge | TopEdge | RightEdge);
                }
                node.First = first;
                node.Second = second;
                node = first;
            }
        }

        /// <summary>Texture2D.PackTextures: the same texture given twice takes one place; a missing
        /// one gets an empty rect; without any texture the atlas stays as it was; null (the atlas
        /// unchanged) when the textures do not fit. Its format is <see cref="AtlasFormat"/>'s, its
        /// colour space its own. A block-compressed atlas takes each texture's levels block for
        /// block (<see cref="CopyBlocks"/>); any other is drawn from the textures' first levels,
        /// with mipmaps when any texture has them (rebuilt from the first level). Returns the
        /// rects in [0, 1].</summary>
        internal static UnityEngine.Rect[] Pack(UnityEngine.Texture2D atlas, UnityEngine.Texture2D[] textures, int padding, int maximumSize, bool makeNoLongerReadable)
        {
            var rects = new UnityEngine.Rect[textures.Length];
            var unique = new List<UnityEngine.Texture2D>();
            var slot = new int[textures.Length];
            for (int i = 0; i < textures.Length; i++)
            {
                var texture = textures[i];
                if (texture == null) { slot[i] = -1; continue; }
                slot[i] = unique.IndexOf(texture);
                if (slot[i] < 0) { slot[i] = unique.Count; unique.Add(texture); }
            }
            if (unique.Count == 0) return rects;
            int n = unique.Count;
            var format = AtlasFormat(unique, padding, maximumSize, out bool mipmaps);
            bool blocks = BlockFormats.BlockBytes(format) > 0;
            var widths = new int[n];
            var heights = new int[n];
            var xs = new int[n];
            var ys = new int[n];
            for (int i = 0; i < n; i++) { widths[i] = unique[i].width; heights[i] = unique[i].height; }
            if (!Layout(widths, heights, padding, maximumSize, xs, ys, out int width, out int height, blocks)) return null;
            if (blocks && (Shrunk(unique, widths, heights) || width < BlockSide || height < BlockSide))
            {
                // Unmeasured for a compressed atlas: textures the layout shrinks to fit the maximum
                // size, or an atlas side under one block (ASTC textures under one block alone).
                Stubs.Ignored("Texture2D.PackTextures of block-compressed textures shrunk to fit or under one block", "packs them uncompressed (RGBA32)");
                format = UnityEngine.TextureFormat.RGBA32;
                blocks = false;
                for (int i = 0; i < n; i++) { widths[i] = unique[i].width; heights[i] = unique[i].height; }
                if (!Layout(widths, heights, padding, maximumSize, xs, ys, out width, out height)) return null;
            }
            var placed = new UnityEngine.Rect[n];
            for (int i = 0; i < n; i++)
            {
                float left = xs[i] / (float)width, bottom = ys[i] / (float)height;
                placed[i] = new UnityEngine.Rect(left, bottom, (widths[i] + xs[i]) / (float)width - left, (heights[i] + ys[i]) / (float)height - bottom);
            }
            if (blocks)
                CopyBlocks(atlas, unique, xs, ys, width, height, format, mipmaps ? BlockMipCount(unique) : 1);
            else
            {
                if (!atlas.Reinitialize(width, height, format, mipmaps)) return null;
                var pixels = new UnityEngine.Color32[width * height];
                for (int i = 0; i < n; i++) Blit(unique[i], widths[i], heights[i], pixels, width, xs[i], ys[i]);
                atlas.SetPixels32(pixels);
            }
            for (int i = 0; i < textures.Length; i++)
                if (slot[i] >= 0) rects[i] = placed[slot[i]];
            atlas.Apply(!blocks, makeNoLongerReadable);
            return rects;
        }

        /// <summary>The atlas's format, as the Unity 6000.0.71f1 Editor chooses it (tests/projects/
        /// packtextures). The first texture's format when it is DXT1, DXT5, BC7, ETC (RGB), ETC2 RGB,
        /// ETC2 RGBA8, ASTC 4×4 or a 4444 format, else RGBA32; each texture keeps it when of the
        /// same format, where DXT1 with DXT5 makes DXT5, ETC with ETC2 RGB makes ETC2 RGB and either
        /// with ETC2 RGBA8 makes ETC2 RGBA8, the two 4444 formats go together, and anything else, or
        /// a texture that must shrink to the maximum size, makes RGBA32. A block-compressed format
        /// stays only when no texture has mipmaps, or all have them and there is no padding; a 4444
        /// format stays either way (the Scripting API page says such sets make RGBA32; the Editor
        /// keeps 4444: `argb4444mixedmips`, `argb4444padding`). The 4444 formats make the one the
        /// graphics device samples, ARGB4444 before RGBA4444 (else RGBA32).
        /// <paramref name="mipmaps"/>: any texture has them.</summary>
        internal static UnityEngine.TextureFormat AtlasFormat(List<UnityEngine.Texture2D> textures, int padding, int maximumSize, out bool mipmaps)
        {
            int max = NextPowerOfTwo(Math.Clamp(maximumSize, 8, UnityEngine.SystemInfo.maxTextureSize));
            var format = Candidate(textures[0].format);
            bool withMips = false, withoutMips = false;
            foreach (var texture in textures)
            {
                if (FitSide(texture.width, max) != texture.width || FitSide(texture.height, max) != texture.height)
                    format = UnityEngine.TextureFormat.RGBA32;
                format = Merge(format, texture.format);
                if (texture.mipmapCount > 1) withMips = true;
                else withoutMips = true;
            }
            mipmaps = withMips;
            if (BlockFormats.BlockBytes(format) > 0 && withMips && (withoutMips || padding >= 1))
                return UnityEngine.TextureFormat.RGBA32;
            if (format is UnityEngine.TextureFormat.ARGB4444 or UnityEngine.TextureFormat.RGBA4444)
                return UnityEngine.SystemInfo.IsFormatSupported(GraphicsFormat.B4G4R4A4_UNormPack16, GraphicsFormatUsage.Sample) ? UnityEngine.TextureFormat.ARGB4444
                    : UnityEngine.SystemInfo.IsFormatSupported(GraphicsFormat.R4G4B4A4_UNormPack16, GraphicsFormatUsage.Sample) ? UnityEngine.TextureFormat.RGBA4444
                    : UnityEngine.TextureFormat.RGBA32;
            return format;
        }

        static UnityEngine.TextureFormat Candidate(UnityEngine.TextureFormat format) => format switch
        {
            UnityEngine.TextureFormat.ARGB4444 or UnityEngine.TextureFormat.RGBA4444 or UnityEngine.TextureFormat.DXT1 or UnityEngine.TextureFormat.DXT5
                or UnityEngine.TextureFormat.BC7 or UnityEngine.TextureFormat.ETC_RGB4 or UnityEngine.TextureFormat.ETC2_RGB
                or UnityEngine.TextureFormat.ETC2_RGBA8 or UnityEngine.TextureFormat.ASTC_4x4 => format,
            _ => UnityEngine.TextureFormat.RGBA32,
        };

        static UnityEngine.TextureFormat Merge(UnityEngine.TextureFormat atlas, UnityEngine.TextureFormat texture)
        {
            const UnityEngine.TextureFormat Rgba32 = UnityEngine.TextureFormat.RGBA32;
            switch (atlas)
            {
                case UnityEngine.TextureFormat.ARGB4444 or UnityEngine.TextureFormat.RGBA4444:
                    return texture is UnityEngine.TextureFormat.ARGB4444 or UnityEngine.TextureFormat.RGBA4444 ? atlas : Rgba32;
                case UnityEngine.TextureFormat.DXT1:
                    return texture is UnityEngine.TextureFormat.DXT1 or UnityEngine.TextureFormat.DXT5 ? texture : Rgba32;
                case UnityEngine.TextureFormat.DXT5:
                    return texture is UnityEngine.TextureFormat.DXT1 or UnityEngine.TextureFormat.DXT5 ? atlas : Rgba32;
                case UnityEngine.TextureFormat.BC7:
                case UnityEngine.TextureFormat.ASTC_4x4:
                    return texture == atlas ? atlas : Rgba32;
                case UnityEngine.TextureFormat.ETC_RGB4:
                    return texture is UnityEngine.TextureFormat.ETC_RGB4 or UnityEngine.TextureFormat.ETC2_RGB or UnityEngine.TextureFormat.ETC2_RGBA8 ? texture : Rgba32;
                case UnityEngine.TextureFormat.ETC2_RGB:
                    return texture is UnityEngine.TextureFormat.ETC_RGB4 or UnityEngine.TextureFormat.ETC2_RGB ? atlas
                        : texture == UnityEngine.TextureFormat.ETC2_RGBA8 ? texture : Rgba32;
                case UnityEngine.TextureFormat.ETC2_RGBA8:
                    return texture is UnityEngine.TextureFormat.ETC_RGB4 or UnityEngine.TextureFormat.ETC2_RGB or UnityEngine.TextureFormat.ETC2_RGBA8 ? atlas : Rgba32;
                default:
                    return Rgba32;
            }
        }

        static bool Shrunk(List<UnityEngine.Texture2D> textures, int[] widths, int[] heights)
        {
            for (int i = 0; i < textures.Count; i++)
                if (widths[i] != textures[i].width || heights[i] != textures[i].height) return true;
            return false;
        }

        /// <summary>The levels of a block-compressed atlas with mipmaps: down to where the smallest
        /// side of any texture is one block (tests/projects/packtextures).</summary>
        static int BlockMipCount(List<UnityEngine.Texture2D> textures)
        {
            int smallest = int.MaxValue;
            foreach (var texture in textures) smallest = Math.Min(smallest, Math.Min(texture.width, texture.height));
            int blocks = smallest / BlockSide, levels = 0;
            while (blocks > 0) { levels++; blocks >>= 1; }
            // (A side under one block, possible for ASTC alone, is unmeasured: one level.)
            return Math.Max(1, levels);
        }

        /// <summary>The atlas's blocks: zero (an empty block) but where a texture lies, where each
        /// level of the texture is copied block for block, as far as the atlas has levels, the
        /// texture's place is on whole blocks there and the texture is at least two blocks wide and
        /// high in the level before (a texture whose place leaves the blocks has no lower levels: its
        /// neighbours' blocks or empty ones show there). DXT1 blocks go into a DXT5 atlas and ETC2
        /// RGB ones into an ETC2 RGBA8 atlas behind an opaque alpha block (eight bytes of 0xFF).
        /// Matches the Unity 6000.0.71f1 Editor, tests/projects/packtextures (`dxt5offblock`).</summary>
        static void CopyBlocks(UnityEngine.Texture2D atlas, List<UnityEngine.Texture2D> textures, int[] xs, int[] ys, int width, int height,
            UnityEngine.TextureFormat format, int levels)
        {
            int blockBytes = BlockFormats.BlockBytes(format);
            var offsets = new int[levels + 1];
            for (int mip = 0; mip < levels; mip++)
                offsets[mip + 1] = offsets[mip] + BlockFormats.LevelSize(format, Math.Max(1, width >> mip), Math.Max(1, height >> mip));
            var data = new byte[offsets[levels]];
            for (int i = 0; i < textures.Count; i++)
            {
                var texture = textures[i];
                var from = texture.format;
                // ETC blocks are ETC2 RGB blocks (unmeasured for this atlas: the Editor reports ETC
                // textures made at run time as ETC2 RGB).
                bool same = from == format || (format == UnityEngine.TextureFormat.ETC2_RGB && from == UnityEngine.TextureFormat.ETC_RGB4);
                bool opaqueAlpha = (format == UnityEngine.TextureFormat.DXT5 && from == UnityEngine.TextureFormat.DXT1)
                    || (format == UnityEngine.TextureFormat.ETC2_RGBA8 && from is UnityEngine.TextureFormat.ETC_RGB4 or UnityEngine.TextureFormat.ETC2_RGB);
                int x = xs[i], y = ys[i], w = texture.width, h = texture.height;
                int copied = Math.Min(texture.mipmapCount, levels);
                for (int mip = 0; mip < copied; mip++)
                {
                    var level = texture.BlockLevel(mip);
                    if (level.IsEmpty) break;
                    int sourceBlocksX = (Math.Max(1, texture.width >> mip) + BlockSide - 1) / BlockSide;
                    int sourceBlocksY = (Math.Max(1, texture.height >> mip) + BlockSide - 1) / BlockSide;
                    int atlasBlocksX = (Math.Max(1, width >> mip) + BlockSide - 1) / BlockSide;
                    int sourceBytes = opaqueAlpha ? OpaqueAlphaBytes : blockBytes;
                    for (int by = 0; by < sourceBlocksY; by++)
                        for (int bx = 0; bx < sourceBlocksX; bx++)
                        {
                            var source = level.Slice((by * sourceBlocksX + bx) * sourceBytes, sourceBytes);
                            var target = data.AsSpan(offsets[mip] + ((y / BlockSide + by) * atlasBlocksX + x / BlockSide + bx) * blockBytes, blockBytes);
                            if (opaqueAlpha)
                            {
                                target.Slice(0, OpaqueAlphaBytes).Fill(0xFF);
                                source.CopyTo(target.Slice(OpaqueAlphaBytes));
                            }
                            else source.CopyTo(target);
                        }
                    x /= 2;
                    y /= 2;
                    if (((x | y) & (BlockSide - 1)) != 0 || w < 2 * BlockSide || h < 2 * BlockSide) break;
                    w /= 2;
                    h /= 2;
                }
            }
            atlas.InitLevels(width, height, format, levels);
            atlas.LoadRawTextureData(data);
        }

        /// <summary>The 8-byte half of a 16-byte block a DXT1 or ETC2 RGB block fills; the other
        /// half is an opaque alpha block.</summary>
        const int OpaqueAlphaBytes = 8;

        /// <summary>A texture's first level into the atlas at (x, y), scaled bilinearly when it
        /// had to shrink.</summary>
        static void Blit(UnityEngine.Texture2D texture, int w, int h, UnityEngine.Color32[] into, int atlasWidth, int x, int y)
        {
            var source = texture.EnginePixels32();
            int sw = texture.width, sh = texture.height;
            if (source == null || source.Length < sw * sh) return;
            for (int row = 0; row < h; row++)
                for (int column = 0; column < w; column++)
                {
                    UnityEngine.Color32 c;
                    if (w == sw && h == sh) c = source[row * sw + column];
                    else c = Sample(source, sw, sh, (column + 0.5f) / w, (row + 0.5f) / h);
                    into[(y + row) * atlasWidth + x + column] = c;
                }
        }

        static UnityEngine.Color32 Sample(UnityEngine.Color32[] source, int w, int h, float u, float v)
        {
            float fx = u * w - 0.5f, fy = v * h - 0.5f;
            int x0 = (int)MathF.Floor(fx), y0 = (int)MathF.Floor(fy);
            float tx = fx - x0, ty = fy - y0;
            UnityEngine.Color32 At(int x, int y) => source[Math.Clamp(y, 0, h - 1) * w + Math.Clamp(x, 0, w - 1)];
            var a = UnityEngine.Color32.Lerp(At(x0, y0), At(x0 + 1, y0), tx);
            var b = UnityEngine.Color32.Lerp(At(x0, y0 + 1), At(x0 + 1, y0 + 1), tx);
            return UnityEngine.Color32.Lerp(a, b, ty);
        }
    }
}
