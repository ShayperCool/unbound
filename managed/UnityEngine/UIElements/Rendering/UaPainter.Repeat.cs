using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unbound.UIElements
{
    // Background images placed by background-size, background-repeat and background-position:
    // the image's size in the element, the copies along each axis and their offset, each copy cut
    // to the element (MeshGenerator.DrawRectangleRepeat's rules).
    internal sealed partial class UaPainter
    {
        struct Tile
        {
            public Rect rect, uv;
        }

        readonly List<Tile>[] m_Tiles = { new List<Tile>(), new List<Tile>() };

        static float Axis(Vector2 v, int i) => i == 0 ? v.x : v.y;

        static Rect WithAxis(Rect r, int i, float position, float size)
        {
            if (i == 0)
            {
                r.x = position;
                r.width = size;
            }
            else
            {
                r.y = position;
                r.height = size;
            }
            return r;
        }

        /// <summary>The image's size in the element by background-size; <paramref name="image"/> is
        /// its natural size.</summary>
        internal static Rect ImageSize(Rect image, Rect total, BackgroundSize size)
        {
            if (size.sizeType == BackgroundSizeType.Contain || size.sizeType == BackgroundSizeType.Cover)
            {
                float sx = total.width / image.width, sy = total.height / image.height;
                bool byWidth = size.sizeType == BackgroundSizeType.Contain ? sx < sy : sx > sy;
                var r = image;
                if (byWidth)
                {
                    r.width = total.width;
                    r.height = image.height * total.width / image.width;
                }
                else
                {
                    r.width = image.width * total.height / image.height;
                    r.height = total.height;
                }
                return r;
            }
            var x = size.x;
            var y = size.y;
            if (x.IsNone() && y.IsNone()) return image;
            var result = image;
            if (!x.IsNone() && y.IsAuto())
            {
                if (x.unit == LengthUnit.Percent) result.width = total.width * x.value / 100f;
                else if (x.unit == LengthUnit.Pixel) result.width = x.value;
                else return result;
                result.height = result.width * image.height / image.width;
                return result;
            }
            if (!x.IsNone() && !y.IsNone())
            {
                if (!x.IsAuto())
                {
                    if (x.unit == LengthUnit.Percent) result.width = total.width * x.value / 100f;
                    else if (x.unit == LengthUnit.Pixel) result.width = x.value;
                }
                if (!y.IsAuto())
                {
                    if (y.unit == LengthUnit.Percent) result.height = total.height * y.value / 100f;
                    else if (y.unit == LengthUnit.Pixel) result.height = y.value;
                    if (x.IsAuto()) result.width = result.height * image.width / image.height;
                }
            }
            return result;
        }

        void DrawRectangleRepeat(RectParams p, Rect total, float pixelsPerPoint)
        {
            var size = ImageSize(p.rect, total, p.size);
            if (size.width <= 1e-30f || size.height <= 1e-30f || total.width <= 1e-30f || total.height <= 1e-30f) return;

            // Round repetition with an auto other size keeps the image's aspect.
            if (p.size.x.IsAuto() && p.repeat.y == Repeat.Round)
            {
                int n = Math.Max((int)(total.height / size.height + 0.5f), 1);
                float h = total.height / n;
                size = new Rect(0f, 0f, h * size.width / size.height, h);
            }
            else if (p.size.y.IsAuto() && p.repeat.x == Repeat.Round)
            {
                int n = Math.Max((int)(total.width / size.width + 0.5f), 1);
                float w = total.width / n;
                size = new Rect(0f, 0f, w, w * size.height / size.width);
            }

            var full = new Rect(0f, 0f, 1f, 1f);
            for (int i = 0; i < 2; i++)
            {
                var tiles = m_Tiles[i];
                tiles.Clear();
                var repeat = i == 0 ? p.repeat.x : p.repeat.y;
                var position = i == 0 ? p.positionX : p.positionY;
                float extent = Axis(total.size, i);
                float step = Axis(size.size, i);
                float covered = 0f;
                switch (repeat)
                {
                    case Repeat.NoRepeat:
                        tiles.Add(new Tile { rect = size, uv = full });
                        covered = step;
                        break;
                    case Repeat.Repeat:
                    {
                        int n = (int)((extent + 1f / pixelsPerPoint) / step);
                        if (position.keyword != BackgroundPositionKeyword.Center) n += 2;
                        else n += (n & 1) == 1 ? 2 : 1;
                        for (int k = 0; k < n; k++)
                        {
                            tiles.Add(new Tile { rect = WithAxis(size, i, k * step, step), uv = full });
                            covered += step;
                        }
                        break;
                    }
                    case Repeat.Space:
                    {
                        int n = (int)(extent / step);
                        if (n >= 0)
                        {
                            tiles.Add(new Tile { rect = WithAxis(size, i, Axis(size.position, i), step), uv = full });
                            covered = step;
                        }
                        if (n >= 2)
                        {
                            tiles.Add(new Tile { rect = WithAxis(size, i, extent - step, step), uv = full });
                            covered = extent;
                        }
                        if (n > 2)
                        {
                            float gap = (extent - step * n) / (n - 1);
                            for (int k = 0; k < n - 2; k++)
                                tiles.Add(new Tile { rect = WithAxis(size, i, (step + gap) * (1 + k), step), uv = full });
                        }
                        break;
                    }
                    case Repeat.Round:
                    {
                        int n = Math.Max((int)((extent + step * 0.5f) / step), 1);
                        float rounded = extent / n;
                        if (position.keyword != BackgroundPositionKeyword.Center) n++;
                        else n += (n & 1) == 1 ? 2 : 1;
                        size = WithAxis(size, i, Axis(size.position, i), rounded);
                        step = rounded;
                        for (int k = 0; k < n; k++)
                        {
                            tiles.Add(new Tile { rect = WithAxis(size, i, rounded * k, rounded), uv = full });
                            covered += rounded;
                        }
                        break;
                    }
                }

                float offset = 0f;
                bool aligned = false;
                if (position.keyword == BackgroundPositionKeyword.Center)
                {
                    offset = (extent - covered) * 0.5f;
                    aligned = true;
                }
                else if (repeat != Repeat.Space)
                {
                    if (position.offset.unit == LengthUnit.Percent)
                    {
                        offset = (extent - step) * position.offset.value / 100f;
                        aligned = true;
                    }
                    else if (position.offset.unit == LengthUnit.Pixel)
                    {
                        offset = position.offset.value;
                    }
                    if (position.keyword == BackgroundPositionKeyword.Right || position.keyword == BackgroundPositionKeyword.Bottom)
                        offset = extent - covered - offset;
                }
                // Images of a whole number of pixels start on a pixel.
                if (aligned && !p.isSprite)
                {
                    float pixels = step * pixelsPerPoint;
                    if (Mathf.Abs(Mathf.Round(pixels) - pixels) < 0.001f) offset = UaLayout.CeilToPixelGrid(offset, pixelsPerPoint);
                }
                if ((repeat == Repeat.Repeat || repeat == Repeat.Round) && step > 1e-30f)
                {
                    if (offset < -step) offset += (int)(-offset / step) * step;
                    if (offset > 0f) offset -= (1 + (int)(offset / step)) * step;
                }
                for (int k = 0; k < tiles.Count; k++)
                {
                    var t = tiles[k];
                    t.rect = WithAxis(t.rect, i, Axis(t.rect.position, i) + offset, Axis(t.rect.size, i));
                    tiles[k] = t;
                }
            }

            // Each copy, cut to the element (its uv cut alike, in the texture's bottom-up space).
            var target = size;
            var uv = full;
            foreach (var row in m_Tiles[1])
            {
                target.y = row.rect.y;
                target.height = row.rect.height;
                uv.y = row.uv.y;
                uv.height = row.uv.height;
                if (target.y < total.y)
                {
                    float cut = total.y - target.y, kept = target.height - cut, whole = cut + kept;
                    uv.y = full.y + full.height * cut / whole;
                    uv.height = full.height * kept / whole;
                    target.y = total.y;
                    target.height = kept;
                }
                if (target.yMax > total.yMax)
                {
                    float cut = target.yMax - total.yMax, kept = target.height - cut, whole = kept + cut;
                    uv.height = uv.height * kept / whole;
                    target.height = kept;
                }
                // Top-down rows to the texture's bottom-up v.
                float above = uv.y - full.y, below = full.yMax - uv.yMax;
                uv.y += below - above;
                foreach (var column in m_Tiles[0])
                {
                    target.x = column.rect.x;
                    target.width = column.rect.width;
                    uv.x = column.uv.x;
                    uv.width = column.uv.width;
                    if (target.x < total.x)
                    {
                        float cut = total.x - target.x, kept = target.width - cut, whole = cut + kept;
                        uv.width = uv.width * kept / whole;
                        uv.x = full.x + full.width * cut / whole;
                        target.x = total.x;
                        target.width = kept;
                    }
                    if (target.xMax > total.xMax)
                    {
                        float cut = target.xMax - total.xMax, kept = target.width - cut, whole = kept + cut;
                        uv.width = uv.width * kept / whole;
                        target.width = kept;
                    }
                    Stamp(p, target, total, uv);
                }
            }
        }

        /// <summary>One copy of the image: the part of its sub-rectangle (a sprite's geometry in its
        /// bounds; the whole image for textures) inside the copy, drawn within the element's
        /// rounded rectangle.</summary>
        void Stamp(RectParams p, Rect target, Rect total, Rect targetUV)
        {
            if (target.width < 0.001f || target.height < 0.001f) return;
            var whole = target;
            whole.size /= targetUV.size;
            whole.position -= new Vector2(targetUV.x, 1f - targetUV.y - targetUV.height) * whole.size;
            var sub = p.subRect;
            sub.position = sub.position * whole.size + whole.position;
            sub.size *= whole.size;
            if (p.HasSlices)
            {
                p.backgroundRepeatRect = Rect.zero;
                p.rect = target;
                DrawRectangle(p);
                return;
            }
            var shown = Intersect(sub, target);
            if (shown.width < 0.001f || shown.height < 0.001f) return;
            if (shown.size != sub.size)
            {
                var fraction = shown.size / sub.size;
                var kept = p.uv.size * fraction;
                var removed = p.uv.size - kept;
                if (shown.x > sub.x) p.uv.x += removed.x - (sub.xMax - shown.xMax) / sub.width * p.uv.width;
                if (shown.yMax < sub.yMax) p.uv.y += removed.y - (shown.y - sub.y) / sub.height * p.uv.height;
                p.uv.size = kept;
            }
            p.backgroundRepeatRect = total == shown ? Rect.zero : shown;
            p.rect = total;
            DrawRectangle(p);
        }
    }
}
