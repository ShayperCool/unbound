using System;
using System.Collections.Generic;

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
        internal static bool Layout(int[] widths, int[] heights, int padding, int maximumSize, int[] x, int[] y, out int atlasWidth, out int atlasHeight)
        {
            int count = widths.Length;
            int max = NextPowerOfTwo(Math.Max(8, maximumSize));
            for (int i = 0; i < count; i++)
            {
                widths[i] = FitSide(widths[i], max);
                heights[i] = FitSide(heights[i], max);
            }
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
                        placed[i] = Insert(root, padding, padding, widths[i], heights[i]);
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

        static Node Insert(Node node, float padX, float padY, float width, float height)
        {
            while (true)
            {
                if (node.First != null)
                {
                    var found = Insert(node.First, padX, padY, width, height);
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
                float x = node.X, y = node.Y;
                Node first, second;
                if (leftX <= leftY)
                {
                    float split = height + padY;
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
                    float split = width + padX;
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
        /// unchanged) when the textures do not fit. The atlas is RGBA32 (Unbound does not keep compressed atlases:
        /// see docs/COMPATIBILITY_GAPS.md), with mipmaps when any texture has them (rebuilt from the
        /// first level), its colour space its own. Returns the rects in [0, 1].</summary>
        internal static UnityEngine.Rect[] Pack(UnityEngine.Texture2D atlas, UnityEngine.Texture2D[] textures, int padding, int maximumSize, bool makeNoLongerReadable)
        {
            var rects = new UnityEngine.Rect[textures.Length];
            var unique = new List<UnityEngine.Texture2D>();
            var slot = new int[textures.Length];
            bool mipmaps = false;
            for (int i = 0; i < textures.Length; i++)
            {
                var texture = textures[i];
                if (texture == null) { slot[i] = -1; continue; }
                slot[i] = unique.IndexOf(texture);
                if (slot[i] < 0) { slot[i] = unique.Count; unique.Add(texture); }
                mipmaps |= texture.mipmapCount > 1;
            }
            if (unique.Count == 0) return rects;
            int n = unique.Count;
            var widths = new int[n];
            var heights = new int[n];
            var xs = new int[n];
            var ys = new int[n];
            for (int i = 0; i < n; i++) { widths[i] = unique[i].width; heights[i] = unique[i].height; }
            if (!Layout(widths, heights, padding, maximumSize, xs, ys, out int width, out int height)) return null;
            atlas.Reinitialize(width, height, UnityEngine.TextureFormat.RGBA32, mipmaps);
            var pixels = new UnityEngine.Color32[width * height];
            var placed = new UnityEngine.Rect[n];
            for (int i = 0; i < n; i++)
            {
                Blit(unique[i], widths[i], heights[i], pixels, width, xs[i], ys[i]);
                float left = xs[i] / (float)width, bottom = ys[i] / (float)height;
                placed[i] = new UnityEngine.Rect(left, bottom, (widths[i] + xs[i]) / (float)width - left, (heights[i] + ys[i]) / (float)height - bottom);
            }
            for (int i = 0; i < textures.Length; i++)
                if (slot[i] >= 0) rects[i] = placed[slot[i]];
            atlas.SetPixels32(pixels);
            atlas.Apply(true, makeNoLongerReadable);
            return rects;
        }

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
