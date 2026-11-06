using System;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    /// <summary>
    /// The random write targets of fragment programs (Graphics.SetRandomWriteTarget), by UAV index.
    /// Each is a global buffer the renderer binds to the fragment program's written buffer at that
    /// register, ahead of a buffer of the same name from the draw, the material or the globals
    /// (measured, tests/projects/cmdbuffers uav.*). A program's buffer without a register is at u1,
    /// after its one render target. Targets stay set until cleared, across command buffers.
    /// Render textures are checked and not bound: Unbound's fragment programs cannot write
    /// textures (such a shader fails to compile, with the reason).
    /// </summary>
    internal static class RandomWriteTargets
    {
        internal const int Count = 8;
        // The renderer's name for target i (crates/unbound-app render executor).
        static readonly string[] s_Names = Names();
        static int s_Set;

        static string[] Names()
        {
            var names = new string[Count];
            for (int i = 0; i < Count; i++) names[i] = "<RandomWriteTarget>" + i;
            return names;
        }

        internal static void Set(int index, ulong handle)
        {
            GfxStream.GlobalBuffer(s_Names[index], handle);
            s_Set |= 1 << index;
        }

        internal static void Clear()
        {
            for (int i = 0; i < Count; i++)
                if ((s_Set & (1 << i)) != 0) GfxStream.GlobalBuffer(s_Names[i], 0);
            s_Set = 0;
        }

        /// <summary>Graphics.SetRandomWriteTarget's checks of a buffer, then the binding; the
        /// counter restarts from 0 unless preserved (it does nothing to a buffer without one).</summary>
        internal static void SetBuffer(int index, object buffer, GpuBufferStorage storage, bool valid, bool preserveCounterValue)
        {
            if (buffer == null) throw new ArgumentNullException("uav");
            if (!valid) throw new ObjectDisposedException("uav");
            CheckIndex(index);
            if (!preserveCounterValue) GpuBufferOps.SetCounter(storage, 0);
            Set(index, storage.Handle);
        }

        internal static void CheckIndex(int index)
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException("index", $"must be non-negative less than {Count}.");
        }

        /// <summary>CommandBuffer.SetRandomWriteTarget's checks when recorded.</summary>
        internal static void CheckCommandIndex(int index)
        {
            if (index < 0) throw new IndexOutOfRangeException("index must be positive");
            if (index >= Count) throw new IndexOutOfRangeException($"index ({index}) must be less than the number of random write targets ({Count})");
        }
    }
}

namespace UnityEngine
{
    public partial class Graphics
    {
        public static void SetRandomWriteTarget(int index, ComputeBuffer uav) => SetRandomWriteTarget(index, uav, false);
        public static void SetRandomWriteTarget(int index, ComputeBuffer uav, [Internal.DefaultValue("false")] bool preserveCounterValue) =>
            RandomWriteTargets.SetBuffer(index, uav, uav?.Storage, uav != null && uav.IsValid(), preserveCounterValue);
        public static void SetRandomWriteTarget(int index, GraphicsBuffer uav) => SetRandomWriteTarget(index, uav, false);
        public static void SetRandomWriteTarget(int index, GraphicsBuffer uav, [Internal.DefaultValue("false")] bool preserveCounterValue) =>
            RandomWriteTargets.SetBuffer(index, uav, uav?.Storage, uav != null && uav.IsValid(), preserveCounterValue);

        /// <summary>A render texture is checked (it must allow random writes; null is accepted) and
        /// not bound (see <see cref="RandomWriteTargets"/>): it takes the place of a buffer set at
        /// the index.</summary>
        public static void SetRandomWriteTarget(int index, RenderTexture uav)
        {
            RandomWriteTargets.CheckIndex(index);
            if (uav != null && !uav.enableRandomWrite) throw new ArgumentException("uav RenderTexture must have random write enabled");
            RandomWriteTargets.Set(index, 0);
        }

        public static void ClearRandomWriteTargets() => RandomWriteTargets.Clear();
    }
}
