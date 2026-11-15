using System;

namespace UnityEngine.Rendering
{
    /// <summary>The wind of SpeedTree trees the GPU Resident Drawer draws (its trees take their
    /// wind parameters from their instance data, not from constant buffers).</summary>
    internal static partial class SpeedTreeWindManager
    {
        /// <summary>Writes the wind parameters of the trees of <paramref name="renderersID"/> into
        /// the drawer's upload buffer (see <see cref="SpeedTreeWindSystem.WriteBuffer"/>).</summary>
        public static void UpdateWindAndWriteBufferWindParams(ReadOnlySpan<int> renderersID, SpeedTreeWindParamsBufferIterator windParams, bool history) =>
            SpeedTreeWindSystem.WriteBuffer(renderersID, in windParams, history);
    }

    /// <summary>Where the wind parameters of a run of tree instances go in an upload buffer: the
    /// instance at index i writes parameter p (a float4) at uint offset
    /// <c>uintParamOffsets[p] + uintStride * i</c> of <c>bufferPtr</c>.</summary>
    internal unsafe partial struct SpeedTreeWindParamsBufferIterator
    {
        public IntPtr bufferPtr;
        public fixed int uintParamOffsets[16];
        public int uintStride;
        public int elementOffset;
        public int elementsCount;
    }
}
