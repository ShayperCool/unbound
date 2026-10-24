using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Unbound.Runtime
{
    // The structures of docs/RENDER_PROTOCOL.md, laid out as crates/unbound-runtime/src/render_protocol.rs.

    [StructLayout(LayoutKind.Sequential)]
    internal struct SubMeshPacket
    {
        internal int IndexStart;
        internal int IndexCount;
        internal int Topology;
        internal int BaseVertex;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct MeshPacket
    {
        internal int VertexCount;
        internal Vector3* Positions;
        internal Vector3* Normals;
        internal Vector4* Tangents;
        internal Color32* Colors;
        internal Vector4* Uv0;
        internal Vector4* Uv1;
        internal Vector4* Uv2;
        internal Vector4* Uv3;
        internal int IndexCount;
        internal uint* Indices;
        internal int SubmeshCount;
        internal SubMeshPacket* Submeshes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct TexturePacket
    {
        internal int Width;
        internal int Height;
        internal int Format;
        internal int MipCount;
        internal byte* Data;
        internal long DataLength;
        internal int FilterMode;
        internal int WrapU;
        internal int WrapV;
        internal int AnisoLevel;
        internal int RenderTarget;
        internal int DepthBits;
        /// <summary>Images (6 for a cubemap), each with its mip chain in turn; 0 is 1.</summary>
        internal int Layers;
        /// <summary>TextureDimension (0 is 2D).</summary>
        internal int Dimension;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ImportedTextureInfo
    {
        internal int Width;
        internal int Height;
        internal int MipCount;
        internal int Srgb;
        internal int FilterMode;
        internal int WrapU;
        internal int WrapV;
        internal int AnisoLevel;
        internal int Readable;
        internal int SpriteCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct MaterialPropertyPacket
    {
        internal byte* Name;
        internal int NameLength;
        internal int Kind;
        internal Vector4 Value;
        internal ulong Texture;
        internal Vector4 TextureST;
        /// <summary>A matrix's (16) or an array's floats; Components floats per array element.</summary>
        internal float* Data;
        internal int DataLength;
        internal int Components;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct MaterialPacket
    {
        internal byte* ShaderName;
        internal int ShaderNameLength;
        internal int RenderQueue;
        internal byte* Keywords;
        internal int KeywordsLength;
        internal int PropertyCount;
        internal MaterialPropertyPacket* Properties;
    }

    /// <summary>Handles of meshes, textures and materials on the renderer side.</summary>
    internal static class RenderHandles
    {
        static long s_Next;
        internal static ulong Next() => (ulong)System.Threading.Interlocked.Increment(ref s_Next);
    }
}
