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

    [StructLayout(LayoutKind.Sequential)]
    internal struct CameraPacket
    {
        internal Matrix4x4 View;
        internal Matrix4x4 Projection;
        internal Rect Viewport;
        internal int ClearFlags;
        internal Color ClearColor;
        internal int FirstDraw;
        internal int DrawCount;
        internal ulong TargetTexture;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DrawPacket
    {
        internal ulong Mesh;
        internal int Submesh;
        internal ulong Material;
        internal Matrix4x4 Model;
        internal Color Color;
        internal ulong MainTexture;
        internal Rect Clip;
        internal int ClipEnabled;
        internal int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct FramePacket
    {
        internal int ScreenWidth;
        internal int ScreenHeight;
        internal int ColorSpace;
        internal int CameraCount;
        internal CameraPacket* Cameras;
        internal int DrawCount;
        internal DrawPacket* Draws;
        internal int GlobalCount;
        internal MaterialPropertyPacket* Globals;
        internal byte* Keywords;
        internal int KeywordsLength;
    }

    /// <summary>Handles of meshes, textures and materials on the renderer side.</summary>
    internal static class RenderHandles
    {
        static long s_Next;
        internal static ulong Next() => (ulong)System.Threading.Interlocked.Increment(ref s_Next);
    }
}
