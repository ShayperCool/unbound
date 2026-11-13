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
            paths = Array.Empty<Vector2[]>();
            var request = Sprite.TexelSource(texture, rect);
            if (request == null) return;
            request["rect"] = new[] { rect.x, rect.y, rect.width, rect.height };
            request["detail"] = detail;
            request["tolerance"] = alphaTolerance;
            request["holes"] = holeDetection;
            var json = System.Text.Json.JsonSerializer.Deserialize<OutlineJson>(Native.Call("sprite-outline", System.Text.Json.JsonSerializer.Serialize(request)),
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
            if (json?.Paths == null) return;
            paths = Array.ConvertAll(json.Paths, p => Array.ConvertAll(p, v => new Vector2(v[0], v[1])));
        }
    }
}
