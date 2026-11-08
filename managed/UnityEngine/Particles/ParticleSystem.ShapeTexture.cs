using System;
using Unbound.Runtime;

namespace UnityEngine
{
    // The Shape module's texture. Documented (Unity 6000.0 ShapeModule): `texture` tints the
    // start colours, `textureClipChannel`/`textureClipThreshold` discard particles that spawn
    // where the channel is lower than the threshold, `textureColorAffectsParticles` and
    // `textureAlphaAffectsParticles` apply the RGB and the alpha, `textureBilinearFiltering`
    // combines four neighbouring texels, `textureUVChannel` picks a mesh's UV channel.
    // Measured in the 6000.0.71f1 Editor (tests/projects/particlemeshshape texture/*):
    // - Each shape reads the texture at a coordinate of its own unit shape, before the shape's
    //   position, rotation and scale: Box (all three types) and Rectangle (x + 0.5, y + 0.5);
    //   Circle 0.5 + 0.5·r·(cos, sin) and Cone (base and volume) 0.5 + 0.5·r²·(cos, sin), with r
    //   the sampled radius over the shape's radius; Sphere and Hemisphere the azimuth over a full
    //   turn and (z + 1) / 2 of the direction; Donut 0.5 + 0.5·(the tube offset over the donut
    //   radius) in the ring's (radial, z) plane; Edge (the place along it, 0); meshes the
    //   interpolated coordinate of the UV channel times 0.9999, repeated into [0, 1). A mesh
    //   without that channel discards every particle.
    // - The coordinate is limited to 0.9999 and scaled by the size. Point sampling takes the
    //   texel it falls in. Bilinear filtering starts half a texel lower (not below 0), blends the
    //   texel with its right and upper neighbours (clamped to the last ones, whatever the wrap
    //   mode) by weights of 255ths of the fractions, truncated, each blend a + (b − a)·w / 256
    //   on the colour bytes.
    // - A channel byte below threshold·256 (truncated) clips the texel: its alpha becomes 0.
    //   Otherwise colour and alpha left out read as 255. The texel multiplies the start colour's
    //   bytes, rounded; with a threshold above 0 every particle whose alpha is then 0 is
    //   discarded: born, then removed with the step's deaths. A texture made non-readable still
    //   samples, without a warning.
    // - The setters keep the threshold within [0, 1] and the UV channel at 0 or more.
    public sealed partial class ParticleSystem
    {
        /// <summary>The texture coordinate of the last shape sample, when
        /// <see cref="m_ShapeUvState"/> is <see cref="ShapeUv.Set"/>.</summary>
        Vector2 m_ShapeUv;
        ShapeUv m_ShapeUvState;
        /// <summary>Every texture's pixels, shared by the systems sampling it.</summary>
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Texture2D, ShapeTextureCache> s_ShapeTextures = new();
        /// <summary>The cache of the texture this system sampled last (saves the table lookup).</summary>
        ShapeTextureCache m_ShapeTexture;
        Texture2D m_ShapeTextureKey;
        /// <summary>The last <see cref="InitParticle"/> failed because the shape texture discarded
        /// the particle.</summary>
        bool m_ShapeTextureDiscarded;
        bool m_ShapeTextureUnbatchedClipReported;
        bool m_ShapeTextureUnsupportedReported;

        enum ShapeUv : byte { None, Set, Missing }

        /// <summary>The factor a mesh's texture coordinate takes before it repeats (measured).</summary>
        const float MeshUvScale = 0.9999f;

        /// <summary>A texture's pixels for the shape module, read once per texture content (its
        /// update count), not per particle or per system.</summary>
        internal sealed class ShapeTextureCache
        {
            Texture2D m_Texture;
            uint m_UpdateCount;
            int m_Width, m_Height;
            Color32[] m_Pixels;

            internal bool Prepare(Texture2D texture)
            {
                if (ReferenceEquals(texture, m_Texture) && texture.updateCount == m_UpdateCount && m_Pixels != null
                    && texture.width == m_Width && texture.height == m_Height)
                    return m_Width > 0 && m_Height > 0;
                m_Texture = texture;
                m_UpdateCount = texture.updateCount;
                m_Width = texture.width;
                m_Height = texture.height;
                // The engine's copy: a non-readable texture samples too (measured).
                m_Pixels = texture.EnginePixels32();
                if (m_Pixels.Length < m_Width * m_Height) m_Width = m_Height = 0;
                return m_Width > 0 && m_Height > 0;
            }

            /// <summary>The largest coordinate sampled (measured: 1 reads the last texel).</summary>
            const float MaxCoordinate = 0.9999f;

            uint Texel(int x, int y)
            {
                var c = m_Pixels[Mathf.Clamp(y, 0, m_Height - 1) * m_Width + Mathf.Clamp(x, 0, m_Width - 1)];
                return c.r | (uint)c.g << 8 | (uint)c.b << 16 | (uint)c.a << 24;
            }

            internal Color32 Sample(Vector2 uv, bool bilinear)
            {
                float x = Mathf.Min(uv.x, MaxCoordinate) * m_Width, y = Mathf.Min(uv.y, MaxCoordinate) * m_Height;
                uint texel;
                if (!bilinear) texel = Texel((int)x, (int)y);
                else
                {
                    x = Mathf.Max(x - 0.5f, 0f);
                    y = Mathf.Max(y - 0.5f, 0f);
                    int i = (int)x, j = (int)y;
                    uint wx = (uint)((x - Mathf.Floor(x)) * 255f), wy = (uint)((y - Mathf.Floor(y)) * 255f);
                    uint top = Blend(Texel(i, j), Texel(i + 1, j), wx);
                    uint bottom = Blend(Texel(i, j + 1), Texel(i + 1, j + 1), wx);
                    texel = Blend(top, bottom, wy);
                }
                return new Color32((byte)texel, (byte)(texel >> 8), (byte)(texel >> 16), (byte)(texel >> 24));
            }

            /// <summary>a + (b − a)·w / 256 on the four bytes at once, two to a word (a borrow of
            /// one byte's difference reaches its neighbour).</summary>
            static uint Blend(uint a, uint b, uint w)
            {
                uint low = ((((b & 0xff00ffu) - (a & 0xff00ffu)) * w >> 8) + (a & 0xff00ffu)) & 0xff00ffu;
                uint high = ((((b >> 8) & 0xff00ffu) - ((a & 0xff00ff00u) >> 8)) * w + (a & 0xff00ff00u)) & 0xff00ff00u;
                return low | high;
            }
        }

        /// <summary>Whether the shape type has a measured texture mapping.</summary>
        static bool ShapeTextureMapped(ParticleSystemShapeType type)
        {
            switch (type)
            {
                case ParticleSystemShapeType.Sphere:
                case ParticleSystemShapeType.Hemisphere:
                case ParticleSystemShapeType.Cone:
                case ParticleSystemShapeType.ConeVolume:
                case ParticleSystemShapeType.Box:
                case ParticleSystemShapeType.BoxShell:
                case ParticleSystemShapeType.BoxEdge:
                case ParticleSystemShapeType.Circle:
                case ParticleSystemShapeType.Donut:
                case ParticleSystemShapeType.Rectangle:
                case ParticleSystemShapeType.SingleSidedEdge:
                case ParticleSystemShapeType.Mesh:
                case ParticleSystemShapeType.MeshRenderer:
                case ParticleSystemShapeType.SkinnedMeshRenderer:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>A particle the shape texture discarded outside a start batch (see
        /// FlushRateBatch): in a ring buffer, an older release, Emit with EmitParams, or emission
        /// over distance alone. The batch's rule (each discarded lane, padding lanes too, lowers
        /// the count started) is measured only for batches; here only the discarded particle
        /// leaves, which can keep more particles than Unity.</summary>
        void ReportUnbatchedTextureClip()
        {
            if (m_ShapeTextureUnbatchedClipReported) return;
            m_ShapeTextureUnbatchedClipReported = true;
            Debug.LogWarning("Unbound: particle shape texture clipping outside a start batch (ring buffer, Emit with EmitParams or emission over distance alone) is not measured; Unity may keep fewer particles");
        }

        void SetShapeUv(float u, float v)
        {
            m_ShapeUv = new Vector2(u, v);
            m_ShapeUvState = ShapeUv.Set;
        }

        /// <summary>A mesh sample's texture coordinate: the shape's UV channel interpolated with
        /// the corner weights; <see cref="ShapeUv.Missing"/> without that channel.</summary>
        void SetMeshShapeUv(Mesh mesh, int a, int b, int c, float wa, float wb, float wc)
        {
            int channel = m_Shape.textureUVChannel;
            var uvs = mesh != null && (uint)channel < (uint)mesh.m_UVs.Length ? mesh.m_UVs[channel] : null;
            int n = uvs?.Length ?? 0;
            if ((uint)a >= (uint)n || (uint)b >= (uint)n || (uint)c >= (uint)n)
            {
                m_ShapeUvState = ShapeUv.Missing;
                return;
            }
            float u = uvs[a].x * wa + uvs[b].x * wb + uvs[c].x * wc, v = uvs[a].y * wa + uvs[b].y * wb + uvs[c].y * wc;
            SetShapeUv(Frac(u * MeshUvScale), Frac(v * MeshUvScale));
        }

        /// <summary>Applies the shape texture to a new particle's start colour; false when its clip
        /// channel discards the particle. Call after <see cref="SampleShape"/>.</summary>
        bool ApplyShapeTexture(ref Color32 color)
        {
            var S = m_Shape;
            var texture = S.texture;
            if (!S.enabled || texture == null) return true;
            if (!ShapeTextureMapped(S.shapeType))
            {
                // The obsolete shell types leave the colour as it is (measured); the sprite
                // shapes' mapping is not measured.
                bool sprite = S.shapeType == ParticleSystemShapeType.Sprite || S.shapeType == ParticleSystemShapeType.SpriteRenderer;
                if (sprite && !m_ShapeTextureUnsupportedReported)
                {
                    m_ShapeTextureUnsupportedReported = true;
                    ReportUnsupported("shape texture on a " + S.shapeType + " shape (its texture mapping is not measured)");
                }
                return true;
            }
            if (m_ShapeUvState == ShapeUv.Missing) return false;
            if (m_ShapeUvState != ShapeUv.Set) return true;
            if (!ReferenceEquals(texture, m_ShapeTextureKey))
            {
                m_ShapeTexture = s_ShapeTextures.GetValue(texture, _ => new ShapeTextureCache());
                m_ShapeTextureKey = texture;
            }
            if (!m_ShapeTexture.Prepare(texture)) return true;
            var texel = m_ShapeTexture.Sample(m_ShapeUv, S.textureBilinearFiltering);
            byte clip = S.textureClipChannel switch
            {
                ParticleSystemShapeTextureChannel.Red => texel.r,
                ParticleSystemShapeTextureChannel.Green => texel.g,
                ParticleSystemShapeTextureChannel.Blue => texel.b,
                _ => texel.a,
            };
            int threshold = (int)(S.textureClipThreshold * 256f);
            if (clip < threshold) texel.a = 0;
            else
            {
                if (!S.textureColorAffectsParticles) texel.r = texel.g = texel.b = 255;
                if (!S.textureAlphaAffectsParticles) texel.a = 255;
            }
            color = ParticleColors.Multiply(color, texel);
            return threshold <= 0 || color.a != 0;
        }
    }
}
