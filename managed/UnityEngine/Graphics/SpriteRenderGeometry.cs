using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>The shared binary/YAML Sprite mesh decoder. Stream layout is measured in the
    /// 6000.0.71f1 serialized records, not a documented Sprite API guarantee.</summary>
    internal static class SpriteRenderGeometry
    {
        internal static Vector2[] Positions(int count, byte[] data,
            List<(int Stream, int Offset, int Format, int Dimension)> channels)
        {
            if (count <= 0) throw new FormatException("the sprite mesh has no vertices");
            if (channels.Count == 0 || channels[0].Dimension < 2 || channels[0].Dimension > 3)
                throw new FormatException("the sprite mesh has no planar positions");
            if (channels[0].Format != 0)
                throw new NotSupportedException($"the sprite mesh's positions have vertex format {channels[0].Format}, not 32-bit floats");
            var strides = new int[8];
            foreach (var c in channels)
            {
                if (c.Dimension == 0) continue;
                if (c.Stream < 0 || c.Stream >= strides.Length || c.Offset < 0 || c.Dimension > 4)
                    throw new FormatException($"vertex stream {c.Stream}, offset {c.Offset}, dimension {c.Dimension}");
                int size = c.Format switch
                {
                    0 or 10 or 11 => 4,
                    1 or 4 or 5 or 8 or 9 => 2,
                    2 or 3 or 6 or 7 => 1,
                    _ => throw new NotSupportedException($"vertex format {c.Format}"),
                };
                strides[c.Stream] = Math.Max(strides[c.Stream], checked(c.Offset + size * c.Dimension));
            }
            long start = 0;
            var position = channels[0];
            for (int s = 0; s < position.Stream; s++)
                if (strides[s] > 0) start = (start + (long)strides[s] * count + 15) & ~15L;
            int stride = strides[position.Stream];
            if (start + (long)(count - 1) * stride + position.Offset + position.Dimension * 4 > data.Length)
                throw new FormatException($"the vertex data ({data.Length} bytes) is shorter than {count} positions");
            var vertices = new Vector2[count];
            for (int v = 0; v < count; v++)
            {
                int at = checked((int)(start + (long)v * stride + position.Offset));
                float x = BitConverter.ToSingle(data, at), y = BitConverter.ToSingle(data, at + 4);
                if (!float.IsFinite(x) || !float.IsFinite(y) || position.Dimension == 3 && BitConverter.ToSingle(data, at + 8) != 0)
                    throw new NotSupportedException("the sprite mesh has non-finite or non-planar positions");
                vertices[v] = new Vector2(x, y);
            }
            return vertices;
        }

        internal static ushort[] Indices(byte[] bytes, int first, int count, int vertexCount)
        {
            if (count <= 0 || count % 3 != 0 || first < 0 || first % 2 != 0 || first + (long)count * 2 > bytes.Length)
                throw new FormatException($"{count} indices from byte {first} of a {bytes.Length}-byte index buffer");
            var indices = new ushort[count];
            for (int i = 0; i < count; i++)
            {
                indices[i] = BitConverter.ToUInt16(bytes, first + i * 2);
                if (indices[i] >= vertexCount) throw new FormatException($"index {indices[i]} of {vertexCount} vertices");
            }
            return indices;
        }
    }
}
