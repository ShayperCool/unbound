using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor.Sprites
{
    public sealed partial class SpriteUtility
    {
        /// <summary>What asking for the atlas data of a sprite that is not packed throws (Unity
        /// 6000.0.71f1, tests/projects/spriteutility texture.*.True, uvs.*.True).</summary>
        const string NotPackedMessage = "Could not get atlas data - Sprite is not packed.";

        /// <summary>The texture of a sprite that is not packed (matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/spriteutility texture.*); its atlas data throws. A packed sprite's source and
        /// atlas textures are not measured: reported, the sprite's texture answered.</summary>
        public static Texture2D GetSpriteTexture(Sprite sprite, bool getAtlasData)
        {
            if (sprite == null) throw new ArgumentNullException(nameof(sprite));
            if (!sprite.packed)
            {
                if (getAtlasData) throw new ArgumentException(NotPackedMessage);
                return sprite.texture;
            }
            Stubs.NotImplemented("UnityEditor.Sprites.SpriteUtility.GetSpriteTexture(packed sprite)");
            return sprite.texture;
        }

        /// <summary>The texture coordinates of a sprite that is not packed, its uv (matches the Unity
        /// 6000.0.71f1 Editor, tests/projects/spriteutility uvs.*); its atlas data throws. A packed
        /// sprite's source and atlas coordinates are not measured: reported, the sprite's uv
        /// answered.</summary>
        public static Vector2[] GetSpriteUVs(Sprite sprite, bool getAtlasData)
        {
            if (sprite == null) throw new ArgumentNullException(nameof(sprite));
            if (!sprite.packed)
            {
                if (getAtlasData) throw new ArgumentException(NotPackedMessage);
                return sprite.uv;
            }
            Stubs.NotImplemented("UnityEditor.Sprites.SpriteUtility.GetSpriteUVs(packed sprite)");
            return sprite.uv;
        }

        /// <summary>The corners of a regular polygon of <paramref name="sides"/> sides inscribed in a
        /// width by height box about the origin: the first at the top, then counter-clockwise, each a
        /// step of one sides-th turn from the last (x = -sin, y = cos, scaled to the half sizes); no
        /// sides give the box's four corners from its bottom left (matches the Unity 6000.0.71f1
        /// Editor, tests/projects/spriteutility outline.*; a negative side count, which ends the
        /// Editor, throws).</summary>
        internal static Vector2[] GeneratePolygonOutlineVerticesOfSize(int sides, int width, int height)
        {
            float halfWidth = width * 0.5f, halfHeight = height * 0.5f;
            if (sides == 0)
                return new[]
                {
                    new Vector2(-halfWidth, -halfHeight), new Vector2(halfWidth, -halfHeight),
                    new Vector2(halfWidth, halfHeight), new Vector2(-halfWidth, halfHeight),
                };
            if (sides < 0) throw new ArgumentOutOfRangeException(nameof(sides));
            var points = new Vector2[sides];
            // The angle grows by one step per corner, in single precision (the measured corners
            // of 128 sides keep the sum's rounding); its sine and cosine are taken in double
            // precision and rounded (every measured corner, bit for bit).
            float step = 2f * Mathf.PI / sides, angle = 0f;
            for (int k = 0; k < sides; k++)
            {
                points[k] = new Vector2(-Mathf.Sin(angle) * halfWidth, Mathf.Cos(angle) * halfHeight);
                angle += step;
            }
            return points;
        }

        sealed class OutlineJson { public float[][][] Paths { get; set; } }

        /// <summary>The traced outlines of a texture rect, in pixels from the rect's centre (matches the
        /// Unity 6000.0.71f1 Editor, tests/projects/spriteoutlines): the SVG importer's generated
        /// physics shape traces its rendered texture this way. Without texels to trace, no paths.</summary>
        internal static void GenerateOutline(Texture2D texture, Rect rect, float detail, byte alphaTolerance, bool holeDetection, out Vector2[][] paths)
        {
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            paths = Trace(texture, rect, detail, alphaTolerance, holeDetection, null);
        }

        /// <summary>A sprite's outline in units from its pivot (matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/spriteoutlines): a Polygon-mode sprite's is its mesh's boundary (the
        /// arguments are not used), any other sprite's rect is traced from its texture as
        /// <see cref="GenerateOutline"/> traces it. The PSD and Aseprite importers make their
        /// generated physics shapes with it. Without texels to trace or an empty mesh, no paths.</summary>
        internal static void GenerateOutlineFromSprite(Sprite sprite, float detail, byte alphaTolerance, bool holeDetection, out Vector2[][] paths)
        {
            if (sprite == null) throw new ArgumentNullException(nameof(sprite));
            if (sprite.m_IsPolygon)
            {
                paths = Array.Empty<Vector2[]>();
                if (!Native.IsReady) return;
                var request = new Dictionary<string, object>
                {
                    ["vertices"] = Array.ConvertAll(sprite.m_Vertices ?? Array.Empty<Vector2>(), v => new[] { v.x, v.y }),
                    ["triangles"] = sprite.m_Triangles ?? Array.Empty<ushort>(),
                };
                paths = Paths(Native.Call("sprite-mesh-boundary", System.Text.Json.JsonSerializer.Serialize(request)));
                return;
            }
            paths = Trace(sprite.m_SourceTexture ?? sprite.m_Texture, sprite.m_Rect, detail, alphaTolerance, holeDetection,
                new Dictionary<string, object> { ["pivot"] = sprite.NormalizedPivot(), ["pixelsPerUnit"] = sprite.m_PixelsPerUnit });
        }

        static Vector2[][] Trace(Texture2D texture, Rect rect, float detail, byte alphaTolerance, bool holeDetection, Dictionary<string, object> sprite)
        {
            var request = texture == null ? null : Sprite.TexelSource(texture, rect);
            if (request == null) return Array.Empty<Vector2[]>();
            request["rect"] = new[] { rect.x, rect.y, rect.width, rect.height };
            request["detail"] = detail;
            request["tolerance"] = alphaTolerance;
            request["holes"] = holeDetection;
            if (sprite != null) request["sprite"] = sprite;
            return Paths(Native.Call("sprite-outline", System.Text.Json.JsonSerializer.Serialize(request)));
        }

        static Vector2[][] Paths(string response)
        {
            var json = System.Text.Json.JsonSerializer.Deserialize<OutlineJson>(response,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
            if (json?.Paths == null) return Array.Empty<Vector2[]>();
            return Array.ConvertAll(json.Paths, p => Array.ConvertAll(p, v => new Vector2(v[0], v[1])));
        }
    }
}
