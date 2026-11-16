using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>
    /// The sort of a list of particles to draw, with the package's own kernels
    /// (Shaders/Sort.compute): a list of key and particle pairs, its count in its first element,
    /// becomes the particles' indices by decreasing key, equal keys by increasing index (the
    /// kernels' comparison: one order whatever passes sort it). A list of up to
    /// <see cref="MaxSortedAtOnce"/> elements is sorted in one pass, a longer one in chunks of
    /// that many and then merged pass by pass through a scratch list. What a list's sort runs is
    /// planned once for its capacity (<see cref="Plan"/>), so a sort sets no new value.
    /// </summary>
    internal sealed class VFXSort
    {
        internal const string SortPath = "Packages/com.unity.visualeffectgraph/Shaders/Sort.compute";

        /// <summary>The most elements one pass sorts in a group's shared memory (32 KB of it).</summary>
        internal const uint MaxSortedAtOnce = 4096;
        /// <summary>The threads of a group of the merge passes.</summary>
        const uint MergeThreadsPerGroup = 64;
        /// <summary>The most thread groups one dimension of a dispatch takes.</summary>
        const uint MaxGroupsPerDimension = 0xffff;

        static readonly int s_InputSequence = Shader.PropertyToID("inputSequence");
        static readonly int s_SortedSequence = Shader.PropertyToID("sortedSequence");
        static readonly int s_InstanceOffset = Shader.PropertyToID("instanceOffset");
        static readonly int s_TotalInstanceCount = Shader.PropertyToID("totalInstanceCount");
        static readonly int s_SubArraySize = Shader.PropertyToID("subArraySize");
        static readonly int s_DispatchWidth = Shader.PropertyToID("dispatchWidth");
        static readonly object s_One = 1;

        static VFXSort s_Shared;
        internal static VFXSort Shared => s_Shared ??= new VFXSort();

        ComputeShader m_Shader;
        bool m_Loaded;

        /// <summary>Where a pass reads and writes: the list, the scratch list, or the sorted indices.</summary>
        internal enum Role : byte { List, Scratch, Sorted }

        internal struct Pass
        {
            internal string Kernel;
            /// <summary>The kernel's index in the shader, found at its first run.</summary>
            internal int KernelIndex;
            internal Role From, To;
            internal int X, Y;
            internal object DispatchWidth, SubArraySize;
        }

        /// <summary>The passes that sort a list of <paramref name="capacity"/> elements; null when
        /// it needs a scratch list and has none (<paramref name="scratch"/>).</summary>
        internal static Pass[] Plan(uint capacity, bool scratch)
        {
            if (capacity <= MaxSortedAtOnce)
            {
                string kernel = capacity <= 128 ? "BitonicSort128" : capacity <= 1024 ? "BitonicSort1024" : capacity <= 2048 ? "BitonicSort2048" : "BitonicSort4096";
                return new[] { new Pass { Kernel = kernel, KernelIndex = -1, From = Role.List, To = Role.Sorted, X = 1, Y = 1, DispatchWidth = s_One } };
            }
            if (!scratch) return null;
            var passes = new List<Pass>();
            Groups((capacity + MaxSortedAtOnce - 1) / MaxSortedAtOnce, out int x, out int y);
            passes.Add(new Pass { Kernel = "BitonicPrePass", KernelIndex = -1, From = Role.List, To = Role.Scratch, X = x, Y = y, DispatchWidth = x });
            // Sorted chunks merged two by two until one is the whole list: the passes alternate
            // between the scratch list and the list itself, the last writes the indices.
            uint whole = NextPowerOfTwo(capacity);
            Groups((capacity + MergeThreadsPerGroup - 1) / MergeThreadsPerGroup, out x, out y);
            var from = Role.Scratch;
            for (uint size = MaxSortedAtOnce; size < whole; size *= 2)
            {
                bool last = size * 2 >= whole;
                var to = last ? Role.Sorted : from == Role.Scratch ? Role.List : Role.Scratch;
                passes.Add(new Pass { Kernel = last ? "MergeFinalPass" : "MergePass", KernelIndex = -1, From = from, To = to, X = x, Y = y, DispatchWidth = x, SubArraySize = (int)size });
                from = to;
            }
            return passes.ToArray();
        }

        static uint NextPowerOfTwo(uint n)
        {
            n--;
            n |= n >> 1;
            n |= n >> 2;
            n |= n >> 4;
            n |= n >> 8;
            n |= n >> 16;
            return n + 1;
        }

        /// <summary>A dispatch of <paramref name="groups"/> groups: as many rows as one
        /// dimension's limit asks for, each of the same width.</summary>
        static void Groups(uint groups, out int x, out int y)
        {
            uint rows = (groups + MaxGroupsPerDimension - 1) / MaxGroupsPerDimension;
            y = (int)rows;
            x = (int)((groups + rows - 1) / rows);
        }

        bool Usable()
        {
            if (m_Loaded) return m_Shader != null;
            m_Loaded = true;
            m_Shader = VFXPackageShaders.Load(SortPath, "sorted outputs are not drawn");
            if (m_Shader == null) return false;
            // A batch of one instance, its list at the start of the buffers.
            m_Shader.SetBoxed(s_TotalInstanceCount, s_One);
            return true;
        }

        /// <summary>Runs the passes of a list's sort; <paramref name="instanceOffset"/> is the
        /// list's capacity, boxed once by its owner. False when the package's kernels are missing.</summary>
        internal bool Sort(Pass[] passes, object instanceOffset, GraphicsBuffer list, GraphicsBuffer scratch, GraphicsBuffer sorted)
        {
            if (!Usable()) return false;
            m_Shader.SetBoxed(s_InstanceOffset, instanceOffset);
            for (int i = 0; i < passes.Length; i++)
            {
                ref var pass = ref passes[i];
                if (pass.KernelIndex < 0) pass.KernelIndex = m_Shader.FindKernel(pass.Kernel);
                int kernel = pass.KernelIndex;
                m_Shader.SetBuffer(kernel, s_InputSequence, Buffer(pass.From, list, scratch, sorted));
                m_Shader.SetBuffer(kernel, s_SortedSequence, Buffer(pass.To, list, scratch, sorted));
                m_Shader.SetBoxed(s_DispatchWidth, pass.DispatchWidth);
                if (pass.SubArraySize != null) m_Shader.SetBoxed(s_SubArraySize, pass.SubArraySize);
                m_Shader.Dispatch(kernel, pass.X, pass.Y, 1);
            }
            return true;
        }

        static GraphicsBuffer Buffer(Role role, GraphicsBuffer list, GraphicsBuffer scratch, GraphicsBuffer sorted) =>
            role == Role.List ? list : role == Role.Scratch ? scratch : sorted;
    }
}
