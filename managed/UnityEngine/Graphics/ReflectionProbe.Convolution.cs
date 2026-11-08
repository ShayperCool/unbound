using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    // The glossy mips of a realtime reflection probe's cube (measured: tests/projects/reflectionprobes
    // realtime.*, Unity 6000.0.71f1 Linux OpenGL): once its faces are drawn, a chain of blurs is
    // built mip by mip with Unity's Hidden/CubeBlur (a 6x6 Gaussian over the mip above, wider where
    // a texel covers less of the sphere), and every mip from 1 on is then that chain read with
    // Hidden/CubeCopy at the fractional level matching its roughness (trilinear). The roughness of
    // mip m is m/6; its chain level comes from 2·size·(m/6)^1.9 placed between powers of two. The
    // two passes ping-pong between the cube and a pooled temporary of the same layout, as nothing
    // may sample the mip it draws into.
    public sealed partial class ReflectionProbe
    {
        /// <summary>The exponent of the roughness the chain level follows.</summary>
        const float k_ChainRoughnessExponent = 1.9f;
        /// <summary>The roughness step between mips (UNITY_SPECCUBE_LOD_STEPS = 6).</summary>
        const float k_RoughnessStep = 1f / 6f;
        /// <summary>The chain level of the mips below four texels (the chain's last levels).</summary>
        const float k_SmallMipLevel = 7f;

        static Material s_CubeBlur, s_CubeCopy;
        static Mesh s_FaceQuads;
        static readonly Dictionary<int, object> s_ConvolveProperties = new Dictionary<int, object>();
        static readonly int s_MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int s_TexelId = Shader.PropertyToID("_Texel");
        static readonly int s_LevelId = Shader.PropertyToID("_Level");
        // Boxed once: the per-draw values are a texel size (a power of two), a whole level and
        // each cube size's chain levels.
        static readonly object[] s_TexelSizes = new object[32];
        static readonly object[] s_WholeLevels = new object[32];
        static readonly Dictionary<int, object[]> s_ChainLevels = new Dictionary<int, object[]>();

        /// <summary>Replaces the mips of <paramref name="cube"/> (mip 0 drawn) with its glossy
        /// convolution. Nothing happens to a cube without mips.</summary>
        internal static void ConvolveRealtime(RenderTexture cube)
        {
            if (cube == null || cube.dimension != TextureDimension.Cube || !cube.useMipMap) return;
            int size = cube.width;
            int mips = cube.mipmapCount;
            if (mips < 2) return;
            if (s_CubeBlur == null)
            {
                var blur = Shader.Find("Hidden/CubeBlur");
                var copy = Shader.Find("Hidden/CubeCopy");
                if (blur == null || copy == null)
                {
                    Debug.LogError("Unbound: the built-in Hidden/CubeBlur or Hidden/CubeCopy shader is missing; realtime reflection probes keep box-filtered mips.");
                    return;
                }
                using (ObjectRegistry.AsAssets())
                {
                    s_CubeBlur = new Material(blur) { hideFlags = HideFlags.HideAndDontSave };
                    s_CubeCopy = new Material(copy) { hideFlags = HideFlags.HideAndDontSave };
                    s_FaceQuads = FaceQuads();
                }
            }
            var descriptor = new RenderTextureDescriptor(size, size, cube.graphicsFormat, 0)
            {
                dimension = TextureDimension.Cube,
                useMipMap = true,
                autoGenerateMips = false,
                mipCount = mips,
                sRGB = cube.sRGB,
            };
            var chain = RenderTexture.GetTemporary(descriptor);
            chain.filterMode = FilterMode.Trilinear;
            var active = RenderTexture.active;
            ulong cubeHandle = cube.EnsureCreated(), chainHandle = chain.EnsureCreated();
            ulong mesh = s_FaceQuads.Sync(), blurMaterial = s_CubeBlur.Sync(), copyMaterial = s_CubeCopy.Sync();
            var saved = GfxStream.Current;
            GfxStream.Camera(Matrix4x4.identity, Matrix4x4.identity, saved.Flags);
            var properties = s_ConvolveProperties;
            // The blur chain: odd mips into the temporary from the cube's mip above, even ones back.
            for (int mip = 1; mip < mips; mip++)
            {
                bool odd = (mip & 1) != 0;
                properties[s_MainTexId] = odd ? cube : chain;
                properties[s_TexelId] = TexelSize(size >> mip);
                properties[s_LevelId] = WholeLevel(mip - 1);
                DrawFaces(odd ? chainHandle : cubeHandle, mip, mesh, blurMaterial, properties);
            }
            for (int mip = 0; mip < mips; mip += 2)
                for (int face = 0; face < 6; face++)
                    Graphics.CopyTexture(cube, face, mip, chain, face, mip);
            // Every mip from 1: the chain at its roughness's level.
            var levels = ChainLevels(size, mips);
            properties[s_MainTexId] = chain;
            properties.Remove(s_TexelId);
            for (int mip = 1; mip < mips; mip++)
            {
                properties[s_LevelId] = levels[mip];
                DrawFaces(cubeHandle, mip, mesh, copyMaterial, properties);
            }
            properties.Clear();
            GfxStream.Camera(saved.View, saved.Projection, saved.Flags);
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(chain);
        }

        static void DrawFaces(ulong target, int mip, ulong mesh, ulong material, Dictionary<int, object> properties)
        {
            for (int face = 0; face < 6; face++)
            {
                GfxStream.SetTarget(target, 0, mip, face);
                GfxStream.Draw(mesh, face, material, 0, Matrix4x4.identity, Color.white, properties: properties);
            }
        }

        static object TexelSize(int texels)
        {
            int log = Mathf.Clamp(Mathf.RoundToInt(Mathf.Log(Mathf.Max(texels, 1), 2f)), 0, 31);
            return s_TexelSizes[log] ??= 1f / (1 << log);
        }

        static object WholeLevel(int level) => s_WholeLevels[level] ??= (float)level;

        /// <summary>The chain level each mip of a cube of <paramref name="size"/> reads.</summary>
        static object[] ChainLevels(int size, int mips)
        {
            if (s_ChainLevels.TryGetValue(size, out var levels)) return levels;
            levels = new object[mips];
            float roughness = 0f;
            int texels = size;
            for (int mip = 1; mip < mips; mip++)
            {
                roughness += k_RoughnessStep;
                float level = k_SmallMipLevel;
                if (texels >= 4)
                {
                    float width = 2f * size * MathF.Pow(roughness, k_ChainRoughnessExponent);
                    // Between the powers of two around it: below 2^(k-2) moves down a level.
                    int k = 10;
                    while (k > 0 && width < Pow2(k - 2)) k--;
                    float low = Pow2(k - 2), high = Pow2(k - 1);
                    level = k - 3 + (width - low) / (high - low);
                }
                levels[mip] = level;
                texels >>= 1;
            }
            s_ChainLevels[size] = levels;
            return levels;
        }

        static float Pow2(int e) => e >= 0 ? 1 << e : 1f / (1 << -e);

        /// <summary>Six clip-space quads, one submesh per face, each corner carrying the cube
        /// direction it covers (the face's axis exactly ±1, as Hidden/CubeBlur requires).</summary>
        static Mesh FaceQuads()
        {
            var vertices = new Vector3[24];
            var directions = new List<Vector3>(24);
            for (int face = 0; face < 6; face++)
            {
                var (forward, up) = s_FaceAxes[face];
                var right = Vector3.Cross(up, forward);
                for (int c = 0; c < 4; c++)
                {
                    float x = c < 2 ? -1f : 1f, y = c == 0 || c == 3 ? -1f : 1f;
                    vertices[face * 4 + c] = new Vector3(x, y, 0.5f);
                    directions.Add(forward + right * x - up * y);
                }
            }
            var mesh = new Mesh { name = "Unbound cube face quads", hideFlags = HideFlags.HideAndDontSave, vertices = vertices };
            mesh.SetUVs(0, directions);
            mesh.subMeshCount = 6;
            for (int face = 0; face < 6; face++)
            {
                int b = face * 4;
                mesh.SetTriangles(new[] { b, b + 1, b + 2, b, b + 2, b + 3 }, face);
            }
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return mesh;
        }
    }
}
