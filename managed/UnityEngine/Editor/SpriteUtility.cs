using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor.Sprites
{
    public sealed partial class SpriteUtility
    {
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
        /// generated physics shapes with it. Without texels to trace, no paths.</summary>
        internal static void GenerateOutlineFromSprite(Sprite sprite, float detail, byte alphaTolerance, bool holeDetection, out Vector2[][] paths)
        {
            if (sprite == null) throw new ArgumentNullException(nameof(sprite));
            if (sprite.m_IsPolygon)
            {
                var request = new Dictionary<string, object>
                {
                    ["vertices"] = Array.ConvertAll(sprite.m_Vertices ?? Array.Empty<Vector2>(), v => new[] { v.x, v.y }),
                    ["triangles"] = sprite.m_Triangles ?? Array.Empty<ushort>(),
                };
                paths = Paths(Native.Call("sprite-mesh-boundary", System.Text.Json.JsonSerializer.Serialize(request)));
                return;
            }
            var rect = sprite.m_Rect;
            var pivot = new[] { rect.width > 0 ? sprite.m_Pivot.x / rect.width : 0f, rect.height > 0 ? sprite.m_Pivot.y / rect.height : 0f };
            paths = Trace(sprite.m_SourceTexture ?? sprite.m_Texture, rect, detail, alphaTolerance, holeDetection,
                new Dictionary<string, object> { ["pivot"] = pivot, ["pixelsPerUnit"] = sprite.m_PixelsPerUnit });
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
