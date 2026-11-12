using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace UnityEngine
{
    public sealed partial class Texture2D
    {
        /// <summary>The complete mip chain in the texture's format, sharing the checked storage
        /// used by <see cref="GetPixelData{T}"/>. Elements that do not fit at the end are omitted.</summary>
        public unsafe NativeArray<T> GetRawTextureData<T>() where T : struct
        {
            RequireReadable(pixels: !SourceBlocks);
            EnterStore();
            var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(
                (void*)m_StorePin.AddrOfPinnedObject(), (SourceCrunch ? m_CrunchArchiveLength : m_Store.Length) / UnsafeUtility.SizeOf<T>(), Allocator.None);
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, m_StoreSafety);
            return array;
        }
    }
}
