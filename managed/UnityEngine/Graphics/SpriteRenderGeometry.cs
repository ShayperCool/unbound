using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>The shared binary/YAML Sprite mesh decoder. Stream layout is measured in the
    /// 6000.0.71f1 serialized records, not a documented Sprite API guarantee.</summary>
    internal static class SpriteRenderGeometry
    {
        /// <summary>The channels of a sprite's vertex data besides positions and texture
        /// coordinates, as saved Sprite assets store them (null: the sprite has no such channel).</summary>
        internal sealed class Attributes
        {
            internal Vector3[] Normals;
            internal Vector4[] Tangents;
            internal Color32[] Colors;
            /// <summary>Blend weights and indices together (Unity saves both channels or neither).</summary>
            internal BoneWeight[] Weights;
            /// <summary>Texture coordinate channels 1 to 7 by channel (null when none is saved).</summary>
            internal Vector2[][] TexCoords;
            /// <summary>The stored texture coordinates 0 (zero unless saved as the sprite's own).</summary>
            internal Vector2[] Uv;
        }

        // Channel indices (VertexAttribute order) and formats (VertexAttributeFormat).
        const int PositionChannel = 0, NormalChannel = 1, TangentChannel = 2, ColorChannel = 3, UvChannel = 4,
            LastTexCoordChannel = 11, BlendWeightChannel = 12, BlendIndicesChannel = 13;
        const int Float32 = 0, UNorm8 = 2, UInt32 = 10;

        /// <summary>Each stream's stride and start: the streams follow each other in order, each
        /// starting on a 16-byte boundary; `End` is where the last one ends.</summary>
        static (int[] Strides, long[] Starts, long End) Streams(int count,
            List<(int Stream, int Offset, int Format, int Dimension)> channels)
        {
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
            var starts = new long[strides.Length];
            long end = 0;
            for (int s = 0; s < strides.Length; s++)
            {
                if (strides[s] == 0) continue;
                end = (end + 15) & ~15L;
                starts[s] = end;
                end += (long)strides[s] * count;
            }
            return (strides, starts, end);
        }

        internal static Vector2[] Positions(int count, byte[] data,
            List<(int Stream, int Offset, int Format, int Dimension)> channels)
        {
            if (count <= 0) throw new FormatException("the sprite mesh has no vertices");
            if (channels.Count == 0 || channels[0].Dimension < 2 || channels[0].Dimension > 3)
                throw new FormatException("the sprite mesh has no planar positions");
            if (channels[0].Format != 0)
                throw new NotSupportedException($"the sprite mesh's positions have vertex format {channels[0].Format}, not 32-bit floats");
            var (strides, starts, _) = Streams(count, channels);
            var position = channels[0];
            long start = starts[position.Stream];
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

        /// <summary>The channels of a saved Sprite asset's vertex data besides its positions, in the
        /// formats the 6000.0.71f1 Editor saves (tests/projects/spriteassets): normals and tangents
        /// as floats, colours as four bytes, blend weights as four floats with four 32-bit bone
        /// indices, texture coordinates 1 to 7 as two floats. Texture coordinates 0 are kept as
        /// stored: zero in an ordinary save (the Editor derives the UVs from the sprite's UV
        /// transform), the sprite's own when SetVertexAttribute wrote them. Any other channel,
        /// format or stream size is rejected.</summary>
        internal static Attributes SavedAttributes(int count, byte[] data,
            List<(int Stream, int Offset, int Format, int Dimension)> channels)
        {
            if (channels.Count != 14) throw new NotSupportedException($"Sprite vertex data has {channels.Count} channels, not 14");
            for (int i = 0; i < channels.Count; i++)
            {
                var c = channels[i];
                bool known = i switch
                {
                    PositionChannel => c.Format == Float32 && c.Dimension == 3,
                    UvChannel => c.Format == Float32 && c.Dimension == 2,
                    NormalChannel => c.Dimension == 0 || c.Format == Float32 && c.Dimension == 3,
                    TangentChannel or BlendWeightChannel => c.Dimension == 0 || c.Format == Float32 && c.Dimension == 4,
                    ColorChannel => c.Dimension == 0 || c.Format == UNorm8 && c.Dimension == 4,
                    BlendIndicesChannel => c.Dimension == 0 || c.Format == UInt32 && c.Dimension == 4,
                    // Texture coordinates 1 to 7: two floats, as SetVertexAttribute writes them.
                    > UvChannel and <= LastTexCoordChannel => c.Dimension == 0 || c.Format == Float32 && c.Dimension == 2,
                    _ => c.Dimension == 0,
                };
                if (!known)
                    throw new NotSupportedException($"Sprite vertex channel {(UnityEngine.Rendering.VertexAttribute)i} with format {c.Format} and dimension {c.Dimension} is not supported");
            }
            if ((channels[BlendWeightChannel].Dimension == 0) != (channels[BlendIndicesChannel].Dimension == 0))
                throw new NotSupportedException("Sprite vertex data has blend weights without blend indices or the reverse");
            var (strides, starts, end) = Streams(count, channels);
            if (end != data.Length) throw new NotSupportedException($"Sprite vertex streams take {end} bytes, not {data.Length}");
            int At(int channel, int vertex, int component) =>
                checked((int)(starts[channels[channel].Stream] + (long)vertex * strides[channels[channel].Stream] + channels[channel].Offset + component * 4));
            // Values are kept as saved (the API returns them; drawing uses positions, colours and
            // texture coordinates).
            float F(int channel, int vertex, int component) => BitConverter.ToSingle(data, At(channel, vertex, component));
            var attributes = new Attributes { Uv = new Vector2[count] };
            for (int v = 0; v < count; v++) attributes.Uv[v] = new Vector2(F(UvChannel, v, 0), F(UvChannel, v, 1));
            if (channels[NormalChannel].Dimension != 0)
            {
                attributes.Normals = new Vector3[count];
                for (int v = 0; v < count; v++)
                    attributes.Normals[v] = new Vector3(F(NormalChannel, v, 0), F(NormalChannel, v, 1), F(NormalChannel, v, 2));
            }
            if (channels[TangentChannel].Dimension != 0)
            {
                attributes.Tangents = new Vector4[count];
                for (int v = 0; v < count; v++)
                    attributes.Tangents[v] = new Vector4(F(TangentChannel, v, 0), F(TangentChannel, v, 1), F(TangentChannel, v, 2), F(TangentChannel, v, 3));
            }
            if (channels[ColorChannel].Dimension != 0)
            {
                attributes.Colors = new Color32[count];
                for (int v = 0; v < count; v++)
                {
                    int at = At(ColorChannel, v, 0);
                    attributes.Colors[v] = new Color32(data[at], data[at + 1], data[at + 2], data[at + 3]);
                }
            }
            for (int channel = UvChannel + 1; channel <= LastTexCoordChannel; channel++)
            {
                if (channels[channel].Dimension == 0) continue;
                var values = new Vector2[count];
                for (int v = 0; v < count; v++) values[v] = new Vector2(F(channel, v, 0), F(channel, v, 1));
                attributes.TexCoords ??= new Vector2[LastTexCoordChannel - UvChannel + 1][];
                attributes.TexCoords[channel - UvChannel] = values;
            }
            if (channels[BlendWeightChannel].Dimension != 0)
            {
                attributes.Weights = new BoneWeight[count];
                for (int v = 0; v < count; v++)
                {
                    int Index(int k) => checked((int)BitConverter.ToUInt32(data, At(BlendIndicesChannel, v, k)));
                    attributes.Weights[v] = new BoneWeight
                    {
                        weight0 = F(BlendWeightChannel, v, 0), weight1 = F(BlendWeightChannel, v, 1),
                        weight2 = F(BlendWeightChannel, v, 2), weight3 = F(BlendWeightChannel, v, 3),
                        boneIndex0 = Index(0), boneIndex1 = Index(1), boneIndex2 = Index(2), boneIndex3 = Index(3),
                    };
                }
            }
            return attributes;
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
