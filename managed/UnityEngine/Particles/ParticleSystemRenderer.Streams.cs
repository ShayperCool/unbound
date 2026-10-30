using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    public sealed partial class ParticleSystemRenderer
    {
        // Measured byte conversion used by native particle SIMD geometry. Its quantization
        // differs from Color.linear; the complete input range is checked by the stream probe.
        static readonly byte[] s_LinearParticleColors =
        {
            0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 4,
            4, 4, 4, 4, 5, 5, 5, 5, 5, 6, 6, 6, 6, 7, 7, 7,
            8, 8, 8, 8, 9, 9, 9, 10, 10, 10, 11, 11, 12, 12, 12, 13,
            13, 14, 14, 14, 15, 15, 16, 16, 17, 17, 17, 18, 18, 19, 19, 20,
            21, 21, 22, 22, 23, 23, 24, 24, 25, 26, 26, 27, 27, 28, 29, 29,
            30, 30, 31, 32, 32, 33, 34, 34, 35, 36, 37, 37, 38, 39, 40, 40,
            41, 42, 43, 44, 45, 45, 46, 47, 48, 49, 50, 51, 52, 53, 53, 54,
            55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70,
            71, 72, 73, 75, 76, 77, 78, 79, 80, 82, 83, 84, 85, 86, 88, 89,
            90, 91, 93, 94, 95, 97, 98, 99, 101, 102, 103, 105, 106, 107, 109, 110,
            111, 113, 114, 116, 117, 118, 120, 121, 123, 124, 125, 127, 128, 130, 131, 133,
            134, 136, 137, 139, 141, 142, 144, 145, 147, 149, 150, 152, 154, 156, 157, 159,
            161, 163, 164, 166, 168, 170, 171, 173, 175, 177, 179, 181, 183, 184, 186, 188,
            190, 192, 194, 196, 198, 200, 202, 204, 206, 208, 210, 212, 214, 216, 218, 220,
            222, 224, 227, 229, 231, 233, 235, 237, 239, 242, 244, 246, 248, 250, 253, 255,
        };
        static Color32 LinearParticleColor(Color32 color) => new Color32(s_LinearParticleColors[color.r],
            s_LinearParticleColors[color.g], s_LinearParticleColors[color.b], color.a);

        internal sealed partial class Geometry
        {
            List<ParticleSystemVertexStream> m_Streams;
            bool m_StreamNormals = true, m_StreamColors = true, m_StreamTangents;
            int m_StreamFloats;
            readonly Vector4[][] m_PackedUvs = new Vector4[8][];

            void ConfigureStreams(List<ParticleSystemVertexStream> streams)
            {
                m_Streams = streams; m_StreamNormals = m_StreamColors = m_StreamTangents = false; m_StreamFloats = 0;
                foreach (var stream in streams)
                {
                    if (stream == ParticleSystemVertexStream.Normal) m_StreamNormals = true;
                    else if (stream == ParticleSystemVertexStream.Tangent) m_StreamTangents = true;
                    else if (stream == ParticleSystemVertexStream.Color) m_StreamColors = true;
                    else m_StreamFloats += Width(stream);
                }
                if (m_StreamFloats > 32) throw new NotSupportedException("Unbound: particle vertex streams exceed eight float4 texture coordinates.");
            }

            static int Width(ParticleSystemVertexStream stream)
            {
                switch (stream)
                {
                    case ParticleSystemVertexStream.Position:
                    case ParticleSystemVertexStream.Normal:
                    case ParticleSystemVertexStream.Tangent:
                    case ParticleSystemVertexStream.Color: return 0;
                    case ParticleSystemVertexStream.UV:
                    case ParticleSystemVertexStream.UV2:
                    case ParticleSystemVertexStream.UV3:
                    case ParticleSystemVertexStream.UV4:
                    case ParticleSystemVertexStream.SizeXY:
                    case ParticleSystemVertexStream.StableRandomXY:
                    case ParticleSystemVertexStream.VaryingRandomXY:
                    case ParticleSystemVertexStream.Custom1XY:
                    case ParticleSystemVertexStream.Custom2XY:
                    case ParticleSystemVertexStream.NoiseSumXY:
                    case ParticleSystemVertexStream.NoiseImpulseXY:
                    case ParticleSystemVertexStream.ColorPackedAsTwoFloats: return 2;
                    case ParticleSystemVertexStream.Center:
                    case ParticleSystemVertexStream.SizeXYZ:
                    case ParticleSystemVertexStream.Rotation3D:
                    case ParticleSystemVertexStream.RotationSpeed3D:
                    case ParticleSystemVertexStream.Velocity:
                    case ParticleSystemVertexStream.StableRandomXYZ:
                    case ParticleSystemVertexStream.VaryingRandomXYZ:
                    case ParticleSystemVertexStream.Custom1XYZ:
                    case ParticleSystemVertexStream.Custom2XYZ:
                    case ParticleSystemVertexStream.NoiseSumXYZ:
                    case ParticleSystemVertexStream.NoiseImpulseXYZ:
                    case ParticleSystemVertexStream.MeshAxisOfRotation:
                    case ParticleSystemVertexStream.NextTrailCenter:
                    case ParticleSystemVertexStream.PreviousTrailCenter: return 3;
                    case ParticleSystemVertexStream.StableRandomXYZW:
                    case ParticleSystemVertexStream.VaryingRandomXYZW:
                    case ParticleSystemVertexStream.Custom1XYZW:
                    case ParticleSystemVertexStream.Custom2XYZW: return 4;
                    default: return 1;
                }
            }

            static float StreamRandom(uint seed)
            {
                var random = Random.State.FromSeed(unchecked((int)(seed + 0x3fb09a55u)));
                return random.NextValue();
            }

            void WriteStreams(ParticleSystem ps, ref ParticleSystem.Particle particle, int particleIndex, int first,
                Vector3 position, Vector3 size, Vector3 velocity, float animFrame, Rect nextFrame, Mesh source)
            {
                int channels = (m_StreamFloats + 3) / 4;
                for (int channel = 0; channel < channels; channel++)
                    if (m_PackedUvs[channel] == null || m_PackedUvs[channel].Length < Positions.Length)
                        Array.Resize(ref m_PackedUvs[channel], Positions.Length);
                Vector4 custom1 = ps.VertexCustomData(ref particle, particleIndex, 0), custom2 = ps.VertexCustomData(ref particle, particleIndex, 1);
                for (int vertex = first; vertex < VertexCount; vertex++)
                {
                    for (int channel = 0; channel < channels; channel++) m_PackedUvs[channel][vertex] = Vector4.zero;
                    int cursor = 0, local = vertex - first;
                    foreach (var stream in m_Streams)
                    {
                        int width = Width(stream);
                        if (width == 0) continue;
                        Vector4 value = Vector4.zero;
                        if (stream >= ParticleSystemVertexStream.StableRandomX && stream <= ParticleSystemVertexStream.VaryingRandomXYZW)
                        {
                            uint seed = particle.m_RandomSeed;
                            if (stream >= ParticleSystemVertexStream.VaryingRandomX)
                                seed += unchecked((uint)((particle.Age01 * 100f) * 100000f));
                            for (uint component = 0; component < width; component++) value[(int)component] = StreamRandom(seed + component);
                        }
                        else if (stream >= ParticleSystemVertexStream.Custom1X && stream <= ParticleSystemVertexStream.Custom1XYZW) value = custom1;
                        else if (stream >= ParticleSystemVertexStream.Custom2X && stream <= ParticleSystemVertexStream.Custom2XYZW) value = custom2;
                        else switch (stream)
                        {
                            case ParticleSystemVertexStream.UV: value = Uvs[vertex]; break;
                            case ParticleSystemVertexStream.UV2:
                            case ParticleSystemVertexStream.UV3:
                            case ParticleSystemVertexStream.UV4:
                                int input = (int)stream - (int)ParticleSystemVertexStream.UV;
                                if (source != null)
                                {
                                    var uv = source.m_UVs[input];
                                    value = uv != null && local < uv.Length ? uv[local] : Vector4.zero;
                                }
                                else if (stream == ParticleSystemVertexStream.UV2)
                                {
                                    var corner = s_CornerUvs[local % 4];
                                    value = new Vector4(nextFrame.x + corner.x * nextFrame.width, nextFrame.y + corner.y * nextFrame.height, 0f, 0f);
                                }
                                break;
                            case ParticleSystemVertexStream.AnimBlend: value.x = animFrame - Mathf.Floor(animFrame); break;
                            case ParticleSystemVertexStream.AnimFrame: value.x = animFrame; break;
                            case ParticleSystemVertexStream.Center: value = position; break;
                            case ParticleSystemVertexStream.VertexID: value.x = local; break;
                            case ParticleSystemVertexStream.SizeX:
                            case ParticleSystemVertexStream.SizeXY:
                            case ParticleSystemVertexStream.SizeXYZ: value = size; break;
                            case ParticleSystemVertexStream.Rotation: value.x = particle.m_Rotation.z; break;
                            case ParticleSystemVertexStream.Rotation3D: value = particle.m_Rotation; break;
                            case ParticleSystemVertexStream.RotationSpeed: value.x = particle.m_AngularVelocity.z; break;
                            case ParticleSystemVertexStream.RotationSpeed3D: value = particle.m_AngularVelocity; break;
                            case ParticleSystemVertexStream.Velocity: value = velocity; break;
                            case ParticleSystemVertexStream.Speed: value.x = velocity.magnitude; break;
                            case ParticleSystemVertexStream.AgePercent: value.x = particle.Age01; break;
                            case ParticleSystemVertexStream.InvStartLifetime: value.x = 1f / particle.m_StartLifetime; break;
                            case ParticleSystemVertexStream.MeshIndex: value.x = ps.MeshIndexOf(ref particle); break;
                            case ParticleSystemVertexStream.ParticleIndex: value.x = particleIndex; break;
                            case ParticleSystemVertexStream.ColorPackedAsTwoFloats:
                                var color = Colors[vertex];
                                value.x = color.r + color.g * (.999f / 255f); value.y = color.b + color.a * (.999f / 255f); break;
                            case ParticleSystemVertexStream.MeshAxisOfRotation: value = particle.m_AxisOfRotation; break;
                            case ParticleSystemVertexStream.NoiseSumX:
                            case ParticleSystemVertexStream.NoiseSumXY:
                            case ParticleSystemVertexStream.NoiseSumXYZ:
                            case ParticleSystemVertexStream.NoiseImpulseX:
                            case ParticleSystemVertexStream.NoiseImpulseXY:
                            case ParticleSystemVertexStream.NoiseImpulseXYZ:
                                if (ps.m_Noise.enabled) Stubs.Ignored("ParticleSystemRenderer noise vertex streams", "cannot export active noise results yet");
                                break;
                            default: throw new NotSupportedException($"Unbound: {stream} is not supported in particle vertex streams.");
                        }
                        for (int component = 0; component < width; component++, cursor++)
                            m_PackedUvs[cursor / 4][vertex][cursor % 4] = value[component];
                    }
                }
            }

            void WriteStreamAttributes(Mesh mesh, Matrix4x4 transform, bool identity)
            {
                int count = VertexCount;
                if (m_StreamTangents && count > 0)
                {
                    var tangents = mesh.m_Tangents != null && mesh.m_Tangents.Length == count ? mesh.m_Tangents : new Vector4[count];
                    for (int i = 0; i < count; i++)
                    {
                        var t = Tangents[i];
                        var direction = identity ? new Vector3(t.x, t.y, t.z) : transform.MultiplyVector(new Vector3(t.x, t.y, t.z)).normalized;
                        tangents[i] = new Vector4(direction.x, direction.y, direction.z, t.w);
                    }
                    mesh.m_Tangents = tangents;
                }
                else mesh.m_Tangents = null;
                int floats = m_Streams == null ? 2 : m_StreamFloats;
                for (int channel = 0; channel < 8; channel++)
                {
                    int dimension = Mathf.Clamp(floats - channel * 4, 0, 4);
                    if (dimension == 0 || count == 0) { mesh.m_UVs[channel] = null; mesh.m_UVDimensions[channel] = 0; continue; }
                    var values = mesh.m_UVs[channel] != null && mesh.m_UVs[channel].Length == count ? mesh.m_UVs[channel] : new Vector4[count];
                    Array.Copy(m_Streams == null ? Uvs : m_PackedUvs[channel], values, count);
                    mesh.m_UVs[channel] = values; mesh.m_UVDimensions[channel] = dimension;
                }
            }
        }
    }
}
