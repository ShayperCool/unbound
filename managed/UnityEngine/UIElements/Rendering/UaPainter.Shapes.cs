using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unbound.UIElements
{
    // Backgrounds, borders and textured rectangles as Unity's renderer tessellates them: straight
    // edges on the geometry, rounded corners as quads whose fragments compute their coverage of the
    // corner ellipse from per-vertex circle coordinates (UIR's arc anti-aliasing).
    internal sealed partial class UaPainter
    {
        internal struct Radii
        {
            public Vector2 topLeft, topRight, bottomRight, bottomLeft;

            internal bool Any(float epsilon) =>
                (topLeft.x > epsilon && topLeft.y > epsilon) || (topRight.x > epsilon && topRight.y > epsilon) ||
                (bottomRight.x > epsilon && bottomRight.y > epsilon) || (bottomLeft.x > epsilon && bottomLeft.y > epsilon);

            /// <summary>Radii scaled down together until adjacent ones fit the rectangle's sides.</summary>
            internal Radii Fit(Rect r)
            {
                float f = 1f;
                void Limit(float a, float b, float side)
                {
                    if (a + b > side && a + b > 0f) f = Mathf.Min(f, side / (a + b));
                }
                Limit(topLeft.x, topRight.x, r.width);
                Limit(bottomLeft.x, bottomRight.x, r.width);
                Limit(topLeft.y, bottomLeft.y, r.height);
                Limit(topRight.y, bottomRight.y, r.height);
                if (f >= 1f) return this;
                return new Radii { topLeft = topLeft * f, topRight = topRight * f, bottomRight = bottomRight * f, bottomLeft = bottomLeft * f };
            }
        }

        /// <summary>UIR's rectangle parameters (MeshGenerator.RectangleParams), as far as they are used.</summary>
        struct RectParams
        {
            public Rect rect, uv, subRect, backgroundRepeatRect;
            public Color color;
            public Texture texture;
            public Radii radii;
            public int leftSlice, topSlice, rightSlice, bottomSlice;
            public float sliceScale;
            public Vector4 rectInset;
            public BackgroundPosition positionX, positionY;
            public BackgroundRepeat repeat;
            public BackgroundSize size;
            public bool isSprite;

            internal bool HasSlices => leftSlice > 0 || topSlice > 0 || rightSlice > 0 || bottomSlice > 0;
        }

        static Vector2 RadiusPoints(Length length, Vector2 size)
        {
            float x = length.value, y = length.value;
            if (length.unit == LengthUnit.Percent)
            {
                x = size.x * length.value / 100f;
                y = size.y * length.value / 100f;
            }
            return new Vector2(Mathf.Max(x, 0f), Mathf.Max(y, 0f));
        }

        static Radii RadiiOf(VisualElement ve, ComputedValues s)
        {
            var size = new Vector2(ve.layout.width, ve.layout.height);
            return new Radii
            {
                topLeft = RadiusPoints(s.borderTopLeftRadius, size),
                topRight = RadiusPoints(s.borderTopRightRadius, size),
                bottomRight = RadiusPoints(s.borderBottomRightRadius, size),
                bottomLeft = RadiusPoints(s.borderBottomLeftRadius, size),
            };
        }

        static bool HasRadius(VisualElement ve, ComputedValues s) => RadiiOf(ve, s).Any(0.001f);

        /// <summary>Half a point less of background under each opaque border at least a point wide
        /// (MeshGenerator.AdjustBackgroundSizeForBorders).</summary>
        static Vector4 BorderInset(VisualElement ve)
        {
            // The layout's border widths (on the pixel grid), as Unity reads them from the resolved style.
            var s = ve.resolvedStyle;
            var inset = Vector4.zero;
            if (s.borderLeftWidth >= 1f && s.borderLeftColor.a >= 1f) inset.x = 0.5f;
            if (s.borderTopWidth >= 1f && s.borderTopColor.a >= 1f) inset.y = 0.5f;
            if (s.borderRightWidth >= 1f && s.borderRightColor.a >= 1f) inset.z = 0.5f;
            if (s.borderBottomWidth >= 1f && s.borderBottomColor.a >= 1f) inset.w = 0.5f;
            return inset;
        }

        // ------------------------------------------------------------------ backgrounds

        void DrawBackground(VisualElement ve, ComputedValues s)
        {
            var layout = ve.layout;
            if (layout.width <= 1e-30f || layout.height <= 1e-30f) return;
            var rect = new Rect(0f, 0f, layout.width, layout.height);
            var radii = RadiiOf(ve, s);
            if (s.backgroundColor.a > 1e-30f)
            {
                var p = new RectParams { rect = rect, color = s.backgroundColor, uv = new Rect(0, 0, 1, 1), radii = radii, rectInset = BorderInset(ve) };
                DrawRectangle(p);
            }
            var image = s.backgroundImage;
            if (image.texture == null && image.sprite == null && image.vectorImage == null && image.renderTexture == null) return;

            var slices = new Vector4(s.unitySliceLeft, s.unitySliceTop, s.unitySliceRight, s.unitySliceBottom);
            float sliceScale = s.unitySliceScale;
            var scaleMode = BackgroundPropertyHelper.ResolveUnityBackgroundScaleMode(s.backgroundPositionX, s.backgroundPositionY, s.backgroundRepeat, s.backgroundSize, out bool valid);
            RectParams bg;
            if (image.texture != null)
            {
                bool sliced = Mathf.RoundToInt(slices.x) != 0 || Mathf.RoundToInt(slices.y) != 0 || Mathf.RoundToInt(slices.z) != 0 || Mathf.RoundToInt(slices.w) != 0;
                bg = Textured(rect, new Rect(0, 0, 1, 1), image.texture, !sliced ? ScaleMode.ScaleToFit : (valid ? scaleMode : ScaleMode.StretchToFill));
                bg.rect = new Rect(0f, 0f, image.texture.width, image.texture.height);
            }
            else if (image.sprite != null)
            {
                bool crop = !valid || scaleMode == ScaleMode.ScaleAndCrop;
                bg = SpriteParams(rect, new Rect(0, 0, 1, 1), image.sprite, crop ? ScaleMode.StretchToFill : scaleMode, ref slices);
                if (bg.texture == null) return;
                bg.rect = new Rect(0f, 0f, image.sprite.rect.width, image.sprite.rect.height);
                sliceScale *= ve.elementPanel != null ? ve.elementPanel.referenceSpritePixelsPerUnit / Mathf.Max(0.01f, image.sprite.pixelsPerUnit) : 1f;
            }
            else if (image.renderTexture != null)
            {
                bg = Textured(rect, new Rect(0, 0, 1, 1), image.renderTexture, ScaleMode.ScaleToFit);
                bg.rect = new Rect(0f, 0f, image.renderTexture.width, image.renderTexture.height);
            }
            else
            {
                WarnOnce($"VectorImage backgrounds (element '{ve.name}') are not drawn yet");
                return;
            }
            bg.radii = radii;
            if (slices != Vector4.zero)
            {
                bg.leftSlice = Mathf.RoundToInt(slices.x);
                bg.topSlice = Mathf.RoundToInt(slices.y);
                bg.rightSlice = Mathf.RoundToInt(slices.z);
                bg.bottomSlice = Mathf.RoundToInt(slices.w);
                bg.sliceScale = sliceScale;
                if (s.unitySliceType == SliceType.Tiled) WarnOnce($"tiled slices (element '{ve.name}') are drawn stretched");
            }
            if (slices != Vector4.zero && !valid)
            {
                bg.positionX = BackgroundPropertyHelper.ConvertScaleModeToBackgroundPosition();
                bg.positionY = BackgroundPropertyHelper.ConvertScaleModeToBackgroundPosition();
                bg.repeat = BackgroundPropertyHelper.ConvertScaleModeToBackgroundRepeat();
                bg.size = BackgroundPropertyHelper.ConvertScaleModeToBackgroundSize();
            }
            else
            {
                bg.positionX = s.backgroundPositionX;
                bg.positionY = s.backgroundPositionY;
                bg.repeat = s.backgroundRepeat;
                bg.size = s.backgroundSize;
            }
            bg.color = s.unityBackgroundImageTintColor;
            bg.rectInset = BorderInset(ve);
            DrawRectangleRepeat(bg, rect, ve.scaledPixelsPerPoint);
        }

        static RectParams Textured(Rect rect, Rect uv, Texture texture, ScaleMode scaleMode)
        {
            AdjustUVsForScaleMode(ref rect, ref uv, texture, scaleMode);
            return new RectParams { rect = rect, subRect = new Rect(0, 0, 1, 1), uv = uv, color = Color.white, texture = texture };
        }

        static void AdjustUVsForScaleMode(ref Rect rect, ref Rect uv, Texture texture, ScaleMode scaleMode)
        {
            float imageAspect = Mathf.Abs(texture.width * uv.width / (texture.height * uv.height));
            float rectAspect = rect.width / rect.height;
            switch (scaleMode)
            {
                case ScaleMode.ScaleAndCrop:
                    if (rectAspect > imageAspect)
                    {
                        float h = uv.height * (imageAspect / rectAspect);
                        uv = new Rect(uv.x, uv.y + (uv.height - h) * 0.5f, uv.width, h);
                    }
                    else
                    {
                        float w = uv.width * (rectAspect / imageAspect);
                        uv = new Rect(uv.x + (uv.width - w) * 0.5f, uv.y, w, uv.height);
                    }
                    break;
                case ScaleMode.ScaleToFit:
                    if (rectAspect > imageAspect)
                    {
                        float f = imageAspect / rectAspect;
                        rect = new Rect(rect.xMin + rect.width * (1f - f) * 0.5f, rect.yMin, f * rect.width, rect.height);
                    }
                    else
                    {
                        float f = rectAspect / imageAspect;
                        rect = new Rect(rect.xMin, rect.yMin + rect.height * (1f - f) * 0.5f, rect.width, f * rect.height);
                    }
                    break;
            }
        }

        /// <summary>A sprite as a textured rectangle: its uv rectangle in the texture, placed by
        /// its geometry within its bounds (MeshGenerator.RectangleParams.MakeSprite).</summary>
        static RectParams SpriteParams(Rect container, Rect subRect, Sprite sprite, ScaleMode scaleMode, ref Vector4 slices)
        {
            var bounds = sprite.bounds;
            if (bounds.size.x < 1e-30f || bounds.size.y < 1e-30f || sprite.texture == null) return default;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (var v in sprite.vertices)
            {
                min = Vector2.Min(min, v);
                max = Vector2.Max(max, v);
            }
            var geom = new Rect(min, max - min);
            Vector2 uvMin = new Vector2(float.MaxValue, float.MaxValue), uvMax = new Vector2(float.MinValue, float.MinValue);
            foreach (var v in sprite.uv)
            {
                uvMin = Vector2.Min(uvMin, v);
                uvMax = Vector2.Max(uvMax, v);
            }
            var uvRect = new Rect(uvMin, uvMax - uvMin);
            Rect src = uvRect;
            if (subRect != new Rect(0, 0, 1, 1))
            {
                src = subRect;
                src.position = src.position * uvRect.size + uvRect.position;
                src.size *= uvRect.size;
            }
            // The geometry's place in the bounds, top-down.
            var g = geom;
            g.position = (g.position - (Vector2)bounds.min) / (Vector2)bounds.size;
            g.size /= (Vector2)bounds.size;
            g.y = 1f - g.size.y - g.y;
            float spriteAspect = sprite.rect.width / sprite.rect.height;
            float containerAspect = container.width / container.height;
            switch (scaleMode)
            {
                case ScaleMode.StretchToFill:
                {
                    var size = container.size;
                    container.position += g.position * size;
                    container.size = g.size * size;
                    break;
                }
                case ScaleMode.ScaleToFit:
                    if (containerAspect > spriteAspect)
                    {
                        float f = spriteAspect / containerAspect;
                        container = new Rect(container.xMin + container.width * (1f - f) * 0.5f, container.yMin, f * container.width, container.height);
                    }
                    else
                    {
                        float f = containerAspect / spriteAspect;
                        container = new Rect(container.xMin, container.yMin + container.height * (1f - f) * 0.5f, container.width, f * container.height);
                    }
                    container.position += g.position * container.size;
                    container.size *= g.size;
                    break;
                default:
                {
                    // Scale and crop: the sprite covers the container; the part inside shows.
                    var b = container;
                    if (containerAspect > spriteAspect)
                    {
                        b.height = b.width / spriteAspect;
                        b.y = container.y - (b.height - container.height) / 2f;
                    }
                    else
                    {
                        b.width = b.height * spriteAspect;
                        b.x = container.x - (b.width - container.width) / 2f;
                    }
                    var size = b.size;
                    b.position += g.position * size;
                    b.size = g.size * size;
                    var shown = Intersect(container, b);
                    if (shown.width < 1e-30f || shown.height < 1e-30f)
                    {
                        container = Rect.zero;
                        break;
                    }
                    var part = shown;
                    part.position = (part.position - b.position) / b.size;
                    part.size /= b.size;
                    part.y = 1f - part.size.y - part.y;
                    src.position += part.position * src.size;
                    src.size *= part.size;
                    container = shown;
                    break;
                }
            }
            var border = sprite.border;
            var spriteSlices = new Vector4(border.x, border.w, border.z, border.y);
            if (slices == Vector4.zero) slices = spriteSlices;
            return new RectParams { rect = container, uv = src, subRect = g, color = Color.white, texture = sprite.texture, isSprite = true };
        }

        internal void DrawTexturedRect(Rect rect, Rect uv, Texture texture, ScaleMode scaleMode, Color tint)
        {
            if (texture == null) return;
            var p = Textured(rect, uv, texture, scaleMode);
            p.color = tint;
            DrawRectangle(p);
        }

        internal void DrawSprite(Rect rect, Rect subRect, Sprite sprite, ScaleMode scaleMode, Color tint)
        {
            if (sprite == null) return;
            var slices = Vector4.zero;
            var p = SpriteParams(rect, subRect, sprite, scaleMode, ref slices);
            if (p.texture == null) return;
            p.color = tint;
            DrawRectangle(p);
        }

        internal void DrawSolidRect(Rect rect, Color color) =>
            DrawRectangle(new RectParams { rect = rect, color = color, uv = new Rect(0, 0, 1, 1) });

        /// <summary>One rectangle: solid or textured (its image over <c>backgroundRepeatRect</c>
        /// when set, else over the whole rectangle), nine-sliced when it has slices, inside the
        /// rectangle's rounded corners.</summary>
        void DrawRectangle(RectParams p)
        {
            if (p.rect.width < 1e-30f || p.rect.height < 1e-30f) return;
            Color32 color = p.color;
            if (color.a == 0) return;
            var radii = p.radii.Fit(p.rect);
            var region = p.rect;
            region.xMin += p.rectInset.x;
            region.yMin += p.rectInset.y;
            region.xMax -= p.rectInset.z;
            region.yMax -= p.rectInset.w;
            if (p.texture == null)
            {
                float none = Begin(null);
                FillRounded(p.rect, radii, region, color, VertSolid, none, p.rect, p.uv, AllEdges);
                return;
            }
            float id = Begin(p.texture);
            if (p.HasSlices)
            {
                NineSlice(p, color, id);
                return;
            }
            var image = p.backgroundRepeatRect.width > 0f && p.backgroundRepeatRect.height > 0f ? p.backgroundRepeatRect : p.rect;
            var shown = Intersect(region, image);
            // The element's edges are anti-aliased; where the image's own rectangle cuts it (a
            // background positioned inside the element) the edge is hard.
            int edges = 0;
            if (Mathf.Abs(shown.xMin - region.xMin) < 1e-4f) edges |= EdgeLeft;
            if (Mathf.Abs(shown.yMin - region.yMin) < 1e-4f) edges |= EdgeTop;
            if (Mathf.Abs(shown.xMax - region.xMax) < 1e-4f) edges |= EdgeRight;
            if (Mathf.Abs(shown.yMax - region.yMax) < 1e-4f) edges |= EdgeBottom;
            FillRounded(p.rect, radii, shown, color, VertTexture, id, image, p.uv, edges);
        }

        /// <summary>A nine-sliced image over <c>p.rect</c>: corners at their size (slices in texture
        /// pixels, times the slice scale, over the texture's pixels per point), edges and center
        /// stretched.</summary>
        void NineSlice(RectParams p, Color32 color, float id)
        {
            var r = p.rect;
            var tex = p.texture;
            float ppp = tex is Texture2D t2 && t2.pixelsPerPoint > 0f ? t2.pixelsPerPoint : 1f;
            float scale = (p.sliceScale > 0f ? p.sliceScale : 1f) / ppp;
            float l = p.leftSlice * scale, t = p.topSlice * scale, rr = p.rightSlice * scale, b = p.bottomSlice * scale;
            if (l + rr > r.width && l + rr > 0f)
            {
                float f = r.width / (l + rr);
                l *= f;
                rr *= f;
            }
            if (t + b > r.height && t + b > 0f)
            {
                float f = r.height / (t + b);
                t *= f;
                b *= f;
            }
            var uv = p.uv;
            float uL = p.leftSlice / (float)tex.width, uR = p.rightSlice / (float)tex.width;
            float vT = p.topSlice / (float)tex.height, vB = p.bottomSlice / (float)tex.height;
            float[] xs = { r.xMin, r.xMin + l, r.xMax - rr, r.xMax };
            float[] us = { uv.xMin, uv.xMin + uL, uv.xMax - uR, uv.xMax };
            float[] ys = { r.yMin, r.yMin + t, r.yMax - b, r.yMax };
            float[] vs = { uv.yMax, uv.yMax - vT, uv.yMin + vB, uv.yMin };
            var unused = new Vector4(UnusedArc, UnusedArc, UnusedArc, UnusedArc);
            for (int j = 0; j < 3; j++)
            {
                if (ys[j + 1] - ys[j] <= 1e-30f) continue;
                for (int i = 0; i < 3; i++)
                {
                    if (xs[i + 1] - xs[i] <= 1e-30f) continue;
                    int a = Vertex(new Vector2(xs[i], ys[j]), color, new Vector2(us[i], vs[j]), VertTexture, id, unused, false);
                    int bb = Vertex(new Vector2(xs[i + 1], ys[j]), color, new Vector2(us[i + 1], vs[j]), VertTexture, id, unused, false);
                    int c = Vertex(new Vector2(xs[i + 1], ys[j + 1]), color, new Vector2(us[i + 1], vs[j + 1]), VertTexture, id, unused, false);
                    int d = Vertex(new Vector2(xs[i], ys[j + 1]), color, new Vector2(us[i], vs[j + 1]), VertTexture, id, unused, false);
                    Triangle(a, bb, c);
                    Triangle(c, d, a);
                }
            }
        }

        static readonly List<float> s_Xs = new List<float>(), s_Ys = new List<float>();

        static void Splits(List<float> into, float min, float max, float a, float b, float c, float d)
        {
            into.Clear();
            into.Add(min);
            into.Add(max);
            foreach (var v in new[] { a, b, c, d })
                if (v > min && v < max) into.Add(v);
            into.Sort();
            for (int i = into.Count - 1; i > 0; i--)
                if (into[i] - into[i - 1] <= 1e-6f) into.RemoveAt(i);
        }

        /// <summary>Which corner ellipse of <paramref name="total"/> a point lies in the box of
        /// (with that corner's center and radii), if any.</summary>
        static bool CornerOf(Rect total, in Radii r, float cx, float cy, out Vector2 center, out Vector2 radii)
        {
            if (r.topLeft.x > 0f && r.topLeft.y > 0f && cx < total.xMin + r.topLeft.x && cy < total.yMin + r.topLeft.y)
            {
                center = new Vector2(total.xMin + r.topLeft.x, total.yMin + r.topLeft.y);
                radii = r.topLeft;
                return true;
            }
            if (r.topRight.x > 0f && r.topRight.y > 0f && cx > total.xMax - r.topRight.x && cy < total.yMin + r.topRight.y)
            {
                center = new Vector2(total.xMax - r.topRight.x, total.yMin + r.topRight.y);
                radii = r.topRight;
                return true;
            }
            if (r.bottomRight.x > 0f && r.bottomRight.y > 0f && cx > total.xMax - r.bottomRight.x && cy > total.yMax - r.bottomRight.y)
            {
                center = new Vector2(total.xMax - r.bottomRight.x, total.yMax - r.bottomRight.y);
                radii = r.bottomRight;
                return true;
            }
            if (r.bottomLeft.x > 0f && r.bottomLeft.y > 0f && cx < total.xMin + r.bottomLeft.x && cy > total.yMax - r.bottomLeft.y)
            {
                center = new Vector2(total.xMin + r.bottomLeft.x, total.yMax - r.bottomLeft.y);
                radii = r.bottomLeft;
                return true;
            }
            center = radii = default;
            return false;
        }

        const int EdgeLeft = 1, EdgeTop = 2, EdgeRight = 4, EdgeBottom = 8, AllEdges = 15;
        const float EdgeRadius = 4096f, EdgeSegment = 32f;

        /// <summary>Up to two straight edges a piece of geometry is anti-aliased against: each the
        /// line <c>x = pos</c> (vertical) or <c>y = pos</c>, covering the side its direction points
        /// to. The shader evaluates them as circles too large to bend over a piece (the first as the
        /// outer arc, inside of which is covered, the second as the inner arc, outside of which is).</summary>
        struct EdgeCover
        {
            public int count;
            public bool v0, v1;
            public float p0, p1, d0, d1, dist0, dist1;

            public void Add(bool vertical, float pos, float dir, float distance)
            {
                if (count < 2)
                {
                    if (count == 0) { v0 = vertical; p0 = pos; d0 = dir; dist0 = distance; }
                    else { v1 = vertical; p1 = pos; d1 = dir; dist1 = distance; }
                    count++;
                    return;
                }
                // Keep the two nearest.
                if (distance < dist0 && dist0 >= dist1) { v0 = vertical; p0 = pos; d0 = dir; dist0 = distance; }
                else if (distance < dist1) { v1 = vertical; p1 = pos; d1 = dir; dist1 = distance; }
            }

            public Vector4 Circle(Vector2 p, Vector2 mid)
            {
                var c = new Vector4(UnusedArc, UnusedArc, UnusedArc, UnusedArc);
                if (count > 0)
                {
                    var center = v0 ? new Vector2(p0 + d0 * EdgeRadius, mid.y) : new Vector2(mid.x, p0 + d0 * EdgeRadius);
                    c.x = (p.x - center.x) / EdgeRadius;
                    c.y = (p.y - center.y) / EdgeRadius;
                }
                if (count > 1)
                {
                    var center = v1 ? new Vector2(p1 - d1 * EdgeRadius, mid.y) : new Vector2(mid.x, p1 - d1 * EdgeRadius);
                    c.z = (p.x - center.x) / EdgeRadius;
                    c.w = (p.y - center.y) / EdgeRadius;
                }
                return c;
            }
        }

        /// <summary>Half a pixel in the element's local units, horizontally and vertically, at the
        /// scale the geometry is generated with: the band around a straight edge in which pixels are
        /// partly covered.</summary>
        Vector2 HalfPixel()
        {
            var m = m_Matrix;
            float sx = new Vector2(m.m00, m.m10).magnitude * m_PixelsPerPoint, sy = new Vector2(m.m01, m.m11).magnitude * m_PixelsPerPoint;
            return new Vector2(sx > 1e-6f ? 0.5f / sx : 0f, sy > 1e-6f ? 0.5f / sy : 0f);
        }

        static void AddSplit(List<float> into, float v, float min, float max)
        {
            if (v > min && v < max) into.Add(v);
        }

        static void SortSplits(List<float> into)
        {
            into.Sort();
            for (int i = into.Count - 1; i > 0; i--)
                if (into[i] - into[i - 1] <= 1e-6f) into.RemoveAt(i);
        }

        /// <summary>The part <paramref name="region"/> of the rounded rectangle <paramref name="total"/>,
        /// solid or with the image over <paramref name="image"/> mapped to <paramref name="uv"/>.
        /// Rounded corners are anti-aliased by their arcs; the straight edges in <paramref name="aaEdges"/>
        /// by the coverage of the pixels they cross (geometry reaching half a pixel past them, as
        /// Unity 6 draws rectangles at fractional positions), the others are hard.</summary>
        void FillRounded(Rect total, in Radii radii, Rect region, Color32 color, byte type, float textureId, Rect image, Rect uv, int aaEdges)
        {
            if (region.width <= 1e-30f || region.height <= 1e-30f) return;
            var half = HalfPixel();
            float ex = half.x, ey = half.y;
            bool aaL = (aaEdges & EdgeLeft) != 0 && ex > 0f, aaR = (aaEdges & EdgeRight) != 0 && ex > 0f;
            bool aaT = (aaEdges & EdgeTop) != 0 && ey > 0f, aaB = (aaEdges & EdgeBottom) != 0 && ey > 0f;
            float minX = region.xMin - (aaL ? ex : 0f), maxX = region.xMax + (aaR ? ex : 0f);
            float minY = region.yMin - (aaT ? ey : 0f), maxY = region.yMax + (aaB ? ey : 0f);
            s_Xs.Clear();
            s_Xs.Add(minX);
            s_Xs.Add(maxX);
            foreach (var v in new[] { total.xMin + radii.topLeft.x, total.xMin + radii.bottomLeft.x, total.xMax - radii.topRight.x, total.xMax - radii.bottomRight.x })
                AddSplit(s_Xs, v, minX, maxX);
            s_Ys.Clear();
            s_Ys.Add(minY);
            s_Ys.Add(maxY);
            foreach (var v in new[] { total.yMin + radii.topLeft.y, total.yMin + radii.topRight.y, total.yMax - radii.bottomLeft.y, total.yMax - radii.bottomRight.y })
                AddSplit(s_Ys, v, minY, maxY);
            if (aaL) { AddSplit(s_Xs, region.xMin, minX, maxX); AddSplit(s_Xs, region.xMin + ex, minX, maxX); }
            if (aaR) { AddSplit(s_Xs, region.xMax, minX, maxX); AddSplit(s_Xs, region.xMax - ex, minX, maxX); }
            if (aaT) { AddSplit(s_Ys, region.yMin, minY, maxY); AddSplit(s_Ys, region.yMin + ey, minY, maxY); }
            if (aaB) { AddSplit(s_Ys, region.yMax, minY, maxY); AddSplit(s_Ys, region.yMax - ey, minY, maxY); }
            // Short pieces along anti-aliased edges keep the large circles flat.
            if (aaT || aaB)
                for (float x = minX + EdgeSegment; x < maxX; x += EdgeSegment) s_Xs.Add(x);
            if (aaL || aaR)
                for (float y = minY + EdgeSegment; y < maxY; y += EdgeSegment) s_Ys.Add(y);
            SortSplits(s_Xs);
            SortSplits(s_Ys);
            var xs = s_Xs.ToArray();
            var ys = s_Ys.ToArray();
            for (int j = 0; j + 1 < ys.Length; j++)
            {
                for (int i = 0; i + 1 < xs.Length; i++)
                {
                    float x0 = xs[i], x1 = xs[i + 1], y0 = ys[j], y1 = ys[j + 1];
                    var mid = new Vector2((x0 + x1) * 0.5f, (y0 + y1) * 0.5f);
                    bool arc = CornerOf(total, radii, mid.x, mid.y, out var c, out var r);
                    // Past an edge inset from the rectangle's (the background under a border), a
                    // corner's arc would cover what the inset leaves out.
                    if (arc && ((mid.x < region.xMin && region.xMin > total.xMin + 1e-4f) || (mid.x > region.xMax && region.xMax < total.xMax - 1e-4f)
                        || (mid.y < region.yMin && region.yMin > total.yMin + 1e-4f) || (mid.y > region.yMax && region.yMax < total.yMax - 1e-4f)))
                        continue;
                    var cover = default(EdgeCover);
                    if (!arc)
                    {
                        if (aaL && mid.x < region.xMin + ex) cover.Add(true, region.xMin, 1f, Mathf.Abs(mid.x - region.xMin));
                        if (aaR && mid.x > region.xMax - ex) cover.Add(true, region.xMax, -1f, Mathf.Abs(mid.x - region.xMax));
                        if (aaT && mid.y < region.yMin + ey) cover.Add(false, region.yMin, 1f, Mathf.Abs(mid.y - region.yMin));
                        if (aaB && mid.y > region.yMax - ey) cover.Add(false, region.yMax, -1f, Mathf.Abs(mid.y - region.yMax));
                    }
                    int a = FillVertex(x0, y0, arc, c, r, cover, mid, color, type, textureId, image, uv, region);
                    int b = FillVertex(x1, y0, arc, c, r, cover, mid, color, type, textureId, image, uv, region);
                    int cc = FillVertex(x1, y1, arc, c, r, cover, mid, color, type, textureId, image, uv, region);
                    int d = FillVertex(x0, y1, arc, c, r, cover, mid, color, type, textureId, image, uv, region);
                    Triangle(a, b, cc);
                    Triangle(cc, d, a);
                }
            }
        }

        int FillVertex(float x, float y, bool arc, Vector2 center, Vector2 radii, in EdgeCover cover, Vector2 mid, Color32 color, byte type, float textureId, Rect image, Rect uv, Rect region)
        {
            Vector4 circle;
            if (arc) circle = new Vector4((x - center.x) / radii.x, (y - center.y) / radii.y, UnusedArc, UnusedArc);
            else circle = cover.Circle(new Vector2(x, y), mid);
            var t = Vector2.zero;
            if (type != VertSolid)
            {
                // Past the edge, the edge's texels.
                float ux = Mathf.Clamp(x, region.xMin, region.xMax), uy = Mathf.Clamp(y, region.yMin, region.yMax);
                float fx = image.width != 0f ? (ux - image.x) / image.width : 0f;
                float fy = image.height != 0f ? (uy - image.y) / image.height : 0f;
                t = new Vector2(uv.x + fx * uv.width, uv.y + (1f - fy) * uv.height);
            }
            return Vertex(new Vector2(x, y), color, t, type, textureId, circle, arc || cover.count > 0);
        }

        // ------------------------------------------------------------------ borders

        void DrawBorder(VisualElement ve, ComputedValues s)
        {
            var layout = ve.layout;
            if (layout.width < 1e-30f || layout.height < 1e-30f) return;
            var colors = new Color32[] { s.borderLeftColor, s.borderTopColor, s.borderRightColor, s.borderBottomColor };
            var rs = ve.resolvedStyle;
            var widths = new[] { rs.borderLeftWidth, rs.borderTopWidth, rs.borderRightWidth, rs.borderBottomWidth };
            bool any = false;
            for (int i = 0; i < 4; i++) any |= widths[i] > 0f && colors[i].a > 0;
            if (!any) return;
            var rect = new Rect(0f, 0f, layout.width, layout.height);
            Border(rect, widths, colors, RadiiOf(ve, s).Fit(rect));
        }

        static readonly List<Vector2> s_Poly = new List<Vector2>(), s_PolyA = new List<Vector2>(), s_PolyB = new List<Vector2>();

        /// <summary>A border: the ring between the rounded rectangle and the same inset by the widths
        /// (inner radii the outer ones less the widths), each side its color, corners split along
        /// the line from the outer corner to the inner one.</summary>
        void Border(Rect rect, float[] w, Color32[] colors, Radii radii)
        {
            float wl = w[0], wt = w[1], wr = w[2], wb = w[3];
            var inner = Rect.MinMaxRect(rect.xMin + wl, rect.yMin + wt, rect.xMax - wr, rect.yMax - wb);
            bool hasInner = inner.width > 0f && inner.height > 0f;
            float none = Begin(null);
            // Straight edges, outer and inner, are anti-aliased: the geometry reaches half a pixel
            // past them and the pixels they cross are covered in part.
            var half = HalfPixel();
            float ex = half.x, ey = half.y;
            float minX = rect.xMin - ex, maxX = rect.xMax + ex, minY = rect.yMin - ey, maxY = rect.yMax + ey;
            var xs = new List<float> { minX, maxX };
            var ys = new List<float> { minY, maxY };
            void AddX(float v) { if (v > minX && v < maxX) xs.Add(v); }
            void AddY(float v) { if (v > minY && v < maxY) ys.Add(v); }
            AddX(rect.xMin); AddX(rect.xMax); AddY(rect.yMin); AddY(rect.yMax);
            if (ex > 0f)
            {
                AddX(rect.xMin + ex); AddX(rect.xMax - ex);
                if (hasInner) { AddX(inner.xMin - ex); AddX(inner.xMin + ex); AddX(inner.xMax - ex); AddX(inner.xMax + ex); }
                for (float x = minX + EdgeSegment; x < maxX; x += EdgeSegment) xs.Add(x);
            }
            if (ey > 0f)
            {
                AddY(rect.yMin + ey); AddY(rect.yMax - ey);
                if (hasInner) { AddY(inner.yMin - ey); AddY(inner.yMin + ey); AddY(inner.yMax - ey); AddY(inner.yMax + ey); }
                for (float y = minY + EdgeSegment; y < maxY; y += EdgeSegment) ys.Add(y);
            }
            AddX(rect.xMin + wl); AddX(rect.xMax - wr);
            AddX(rect.xMin + radii.topLeft.x); AddX(rect.xMin + radii.bottomLeft.x);
            AddX(rect.xMax - radii.topRight.x); AddX(rect.xMax - radii.bottomRight.x);
            AddY(rect.yMin + wt); AddY(rect.yMax - wb);
            AddY(rect.yMin + radii.topLeft.y); AddY(rect.yMin + radii.topRight.y);
            AddY(rect.yMax - radii.bottomLeft.y); AddY(rect.yMax - radii.bottomRight.y);
            SortSplits(xs);
            SortSplits(ys);
            for (int j = 0; j + 1 < ys.Count; j++)
            {
                float y0 = ys[j], y1 = ys[j + 1];
                if (y1 - y0 <= 1e-6f) continue;
                for (int i = 0; i + 1 < xs.Count; i++)
                {
                    float x0 = xs[i], x1 = xs[i + 1];
                    if (x1 - x0 <= 1e-6f) continue;
                    float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;
                    bool inInner = hasInner && cx > inner.xMin && cx < inner.xMax && cy > inner.yMin && cy < inner.yMax;
                    bool outerArc = CornerOf(rect, radii, cx, cy, out var center, out var outerRadii);
                    // The straight edges a piece is near: the outer ones, and the inner ones along
                    // their extent (beside the inner box a side's border is solid).
                    var cover = default(EdgeCover);
                    if (!outerArc)
                    {
                        if (ex > 0f && cx < rect.xMin + ex) cover.Add(true, rect.xMin, 1f, Mathf.Abs(cx - rect.xMin));
                        if (ex > 0f && cx > rect.xMax - ex) cover.Add(true, rect.xMax, -1f, Mathf.Abs(cx - rect.xMax));
                        if (ey > 0f && cy < rect.yMin + ey) cover.Add(false, rect.yMin, 1f, Mathf.Abs(cy - rect.yMin));
                        if (ey > 0f && cy > rect.yMax - ey) cover.Add(false, rect.yMax, -1f, Mathf.Abs(cy - rect.yMax));
                        if (hasInner)
                        {
                            bool alongX = cx > inner.xMin && cx < inner.xMax, alongY = cy > inner.yMin && cy < inner.yMax;
                            if (ey > 0f && alongX && Mathf.Abs(cy - inner.yMin) < ey) cover.Add(false, inner.yMin, -1f, Mathf.Abs(cy - inner.yMin));
                            if (ey > 0f && alongX && Mathf.Abs(cy - inner.yMax) < ey) cover.Add(false, inner.yMax, 1f, Mathf.Abs(cy - inner.yMax));
                            if (ex > 0f && alongY && Mathf.Abs(cx - inner.xMin) < ex) cover.Add(true, inner.xMin, -1f, Mathf.Abs(cx - inner.xMin));
                            if (ex > 0f && alongY && Mathf.Abs(cx - inner.xMax) < ex) cover.Add(true, inner.xMax, 1f, Mathf.Abs(cx - inner.xMax));
                        }
                    }
                    bool outside = cx < rect.xMin || cx > rect.xMax || cy < rect.yMin || cy > rect.yMax;
                    if (outside && !outerArc && cover.count == 0) continue;
                    var mid = new Vector2(cx, cy);
                    bool innerArc = false;
                    var innerRadii = Vector2.zero;
                    int corner = Corner(rect, radii, w, cx, cy);
                    if (outerArc && inInner)
                    {
                        // Inside the inner box at a rounded corner: the ring's curved part.
                        innerRadii = corner switch
                        {
                            0 => outerRadii - new Vector2(wl, wt),
                            1 => outerRadii - new Vector2(wr, wt),
                            2 => outerRadii - new Vector2(wr, wb),
                            _ => outerRadii - new Vector2(wl, wb),
                        };
                        innerArc = innerRadii.x > 0f && innerRadii.y > 0f;
                    }
                    if (inInner && !innerArc)
                    {
                        // Inside the inner box: only the half pixel along its edges, as the side there.
                        if (outerArc || cover.count == 0) continue;
                        float dl = cx - inner.xMin, dr = inner.xMax - cx, dt = cy - inner.yMin, db = inner.yMax - cy;
                        float nearest = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(dt, db));
                        var side = nearest == dl ? colors[0] : nearest == dr ? colors[2] : nearest == dt ? colors[1] : colors[3];
                        s_Poly.Clear();
                        s_Poly.Add(new Vector2(x0, y0));
                        s_Poly.Add(new Vector2(x1, y0));
                        s_Poly.Add(new Vector2(x1, y1));
                        s_Poly.Add(new Vector2(x0, y1));
                        EmitPolygon(s_Poly, side, none, false, default, default, false, default, cover, mid);
                        continue;
                    }
                    s_Poly.Clear();
                    s_Poly.Add(new Vector2(x0, y0));
                    s_Poly.Add(new Vector2(x1, y0));
                    s_Poly.Add(new Vector2(x1, y1));
                    s_Poly.Add(new Vector2(x0, y1));
                    if (corner >= 0)
                    {
                        // Split between the corner's horizontal side (top/bottom) and vertical side.
                        Vector2 o, d;
                        Color32 horizontal, vertical;
                        float inward;
                        switch (corner)
                        {
                            case 0: o = new Vector2(rect.xMin, rect.yMin); d = new Vector2(wl, wt); horizontal = colors[1]; vertical = colors[0]; inward = 1f; break;
                            case 1: o = new Vector2(rect.xMax, rect.yMin); d = new Vector2(-wr, wt); horizontal = colors[1]; vertical = colors[2]; inward = -1f; break;
                            case 2: o = new Vector2(rect.xMax, rect.yMax); d = new Vector2(-wr, -wb); horizontal = colors[3]; vertical = colors[2]; inward = -1f; break;
                            default: o = new Vector2(rect.xMin, rect.yMax); d = new Vector2(wl, -wb); horizontal = colors[3]; vertical = colors[0]; inward = 1f; break;
                        }
                        if (d.x == 0f && d.y == 0f) continue;
                        if (d.y == 0f || d.x == 0f)
                        {
                            // One side has no width: the corner is the other side's.
                            EmitPolygon(s_Poly, d.y == 0f ? vertical : horizontal, none, outerArc, center, outerRadii, innerArc, innerRadii, cover, mid);
                            continue;
                        }
                        // Toward the inner corner; on a corner rounded inside too, toward the
                        // corner's center (measured in Unity 6).
                        var cornerRadii = corner switch { 0 => radii.topLeft, 1 => radii.topRight, 2 => radii.bottomRight, _ => radii.bottomLeft };
                        if (cornerRadii.x > Mathf.Abs(d.x) && cornerRadii.y > Mathf.Abs(d.y))
                            d = new Vector2(Mathf.Sign(d.x) * cornerRadii.x, Mathf.Sign(d.y) * cornerRadii.y);
                        // The side of the line that the horizontal edge (moving inward from the
                        // corner) lies on.
                        float hSign = Mathf.Sign(Cross(d, new Vector2(inward, 0f)));
                        ClipHalfPlane(s_Poly, o, d, hSign, s_PolyA);
                        ClipHalfPlane(s_Poly, o, d, -hSign, s_PolyB);
                        EmitPolygon(s_PolyA, horizontal, none, outerArc, center, outerRadii, innerArc, innerRadii, cover, mid);
                        EmitPolygon(s_PolyB, vertical, none, outerArc, center, outerRadii, innerArc, innerRadii, cover, mid);
                        continue;
                    }
                    Color32 color = cx < inner.xMin ? colors[0] : cx > inner.xMax ? colors[2] : cy < inner.yMin ? colors[1] : colors[3];
                    if (outside)
                    {
                        // Past the outer edge (half a pixel): the side it is beyond.
                        color = cx < rect.xMin ? colors[0] : cx > rect.xMax ? colors[2] : cy < rect.yMin ? colors[1] : colors[3];
                    }
                    else if (!hasInner)
                    {
                        // Borders wider than the element: the nearest side.
                        color = cx < rect.xMin + wl ? colors[0] : cx > rect.xMax - wr ? colors[2] : cy < rect.yMin + wt ? colors[1] : colors[3];
                    }
                    EmitPolygon(s_Poly, color, none, outerArc, center, outerRadii, innerArc, innerRadii, cover, mid);
                }
            }
        }

        /// <summary>The corner quadrant (0 top-left, 1 top-right, 2 bottom-right, 3 bottom-left) a
        /// point of the border lies in: within the larger of the corner's radius and the adjacent
        /// widths from both of its sides; -1 on a side.</summary>
        static int Corner(Rect rect, in Radii r, float[] w, float cx, float cy)
        {
            if (cx < rect.xMin + Mathf.Max(r.topLeft.x, w[0]) && cy < rect.yMin + Mathf.Max(r.topLeft.y, w[1])) return 0;
            if (cx > rect.xMax - Mathf.Max(r.topRight.x, w[2]) && cy < rect.yMin + Mathf.Max(r.topRight.y, w[1])) return 1;
            if (cx > rect.xMax - Mathf.Max(r.bottomRight.x, w[2]) && cy > rect.yMax - Mathf.Max(r.bottomRight.y, w[3])) return 2;
            if (cx < rect.xMin + Mathf.Max(r.bottomLeft.x, w[0]) && cy > rect.yMax - Mathf.Max(r.bottomLeft.y, w[3])) return 3;
            return -1;
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        /// <summary>The part of a convex polygon on the <paramref name="sign"/> side of the line
        /// through <paramref name="o"/> along <paramref name="d"/>.</summary>
        static void ClipHalfPlane(List<Vector2> poly, Vector2 o, Vector2 d, float sign, List<Vector2> into)
        {
            into.Clear();
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % n];
                float da = sign * Cross(d, a - o), db = sign * Cross(d, b - o);
                if (da >= 0f) into.Add(a);
                if ((da > 0f && db < 0f) || (da < 0f && db > 0f))
                {
                    float t = da / (da - db);
                    into.Add(a + (b - a) * t);
                }
            }
        }

        void EmitPolygon(List<Vector2> poly, Color32 color, float textureId, bool outerArc, Vector2 center, Vector2 outerRadii, bool innerArc, Vector2 innerRadii, in EdgeCover cover, Vector2 mid)
        {
            if (poly.Count < 3 || color.a == 0) return;
            int first = -1;
            bool arcs = outerArc || innerArc;
            for (int i = 0; i < poly.Count; i++)
            {
                var p = poly[i];
                var circle = arcs ? new Vector4(
                    outerArc ? (p.x - center.x) / outerRadii.x : UnusedArc,
                    outerArc ? (p.y - center.y) / outerRadii.y : UnusedArc,
                    innerArc ? (p.x - center.x) / innerRadii.x : UnusedArc,
                    innerArc ? (p.y - center.y) / innerRadii.y : UnusedArc) : cover.Circle(p, mid);
                int v = Vertex(p, color, Vector2.zero, VertSolid, textureId, circle, arcs || cover.count > 0);
                if (first < 0) first = v;
            }
            for (int i = 1; i + 1 < poly.Count; i++) Triangle(first, first + i, first + i + 1);
        }
    }
}
