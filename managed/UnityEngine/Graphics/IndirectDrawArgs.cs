using System.Runtime.InteropServices;

namespace UnityEngine
{
    public sealed partial class GraphicsBuffer
    {
        /// <summary>
        /// One non-indexed indirect draw command (Scripting API 6000.0,
        /// GraphicsBuffer.IndirectDrawArgs: "Defines the data layout for non-indexed indirect render
        /// calls"), filled on the CPU and written with GraphicsBuffer.SetData into an IndirectArguments
        /// buffer for Graphics.RenderPrimitivesIndirect. Four 32-bit arguments in the order the
        /// shipped UnityIndirect.cginc reads them from the buffer (offsets 0, 4, 8 and 12 of a
        /// 16-byte command), which is also the order of wgpu's non-indexed indirect draws.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public partial struct IndirectDrawArgs
        {
            /// <summary>The size of the struct in bytes.</summary>
            public const int size = 16;

            uint m_VertexCountPerInstance;
            uint m_InstanceCount;
            uint m_StartVertex;
            uint m_StartInstance;

            /// <summary>The number of vertices per instance.</summary>
            public uint vertexCountPerInstance { readonly get => m_VertexCountPerInstance; set => m_VertexCountPerInstance = value; }
            /// <summary>The number of instances to render.</summary>
            public uint instanceCount { readonly get => m_InstanceCount; set => m_InstanceCount = value; }
            /// <summary>The first vertex to render.</summary>
            public uint startVertex { readonly get => m_StartVertex; set => m_StartVertex = value; }
            /// <summary>The first instance to render.</summary>
            public uint startInstance { readonly get => m_StartInstance; set => m_StartInstance = value; }
        }

        /// <summary>
        /// One indexed indirect draw command (Scripting API 6000.0,
        /// GraphicsBuffer.IndirectDrawIndexedArgs: "Defines the data layout for indexed indirect
        /// render calls"), for Graphics.RenderMeshIndirect. Five 32-bit arguments in the order the
        /// shipped UnityIndirect.cginc reads them (offsets 0, 4, 8, 12 and 16 of a 20-byte command),
        /// which is also the order of wgpu's indexed indirect draws.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public partial struct IndirectDrawIndexedArgs
        {
            /// <summary>The size of the struct in bytes.</summary>
            public const int size = 20;

            uint m_IndexCountPerInstance;
            uint m_InstanceCount;
            uint m_StartIndex;
            uint m_BaseVertexIndex;
            uint m_StartInstance;

            /// <summary>The number of vertex indices per instance.</summary>
            public uint indexCountPerInstance { readonly get => m_IndexCountPerInstance; set => m_IndexCountPerInstance = value; }
            /// <summary>The number of instances to render.</summary>
            public uint instanceCount { readonly get => m_InstanceCount; set => m_InstanceCount = value; }
            /// <summary>The first index to use in the index buffer of the rendering call.</summary>
            public uint startIndex { readonly get => m_StartIndex; set => m_StartIndex = value; }
            /// <summary>The index to add to values fetched from the index buffer.</summary>
            public uint baseVertexIndex { readonly get => m_BaseVertexIndex; set => m_BaseVertexIndex = value; }
            /// <summary>The first instance to render.</summary>
            public uint startInstance { readonly get => m_StartInstance; set => m_StartInstance = value; }
        }
    }
}
