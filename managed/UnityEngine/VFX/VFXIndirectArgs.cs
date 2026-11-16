using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>
    /// The compute shaders the Visual Effect Graph package ships for its runtime (the indirect
    /// arguments, the buffer copies), loaded from the package once and run as they are, each
    /// user with a copy of its own (its bindings are that user's).
    /// </summary>
    internal static class VFXPackageShaders
    {
        internal const string CopyBuffer = "Packages/com.unity.visualeffectgraph/Shaders/VFXCopyBuffer.compute";
        internal const string FillIndirectArgs = "Packages/com.unity.visualeffectgraph/Shaders/VFXFillIndirectArgs.compute";

        static readonly HashSet<string> s_Missing = new HashSet<string>();

        /// <summary>A copy of the package's compute shader at <paramref name="path"/>; null when
        /// the project has none, said once for each thing that goes undone (<paramref name="without"/>).</summary>
        internal static ComputeShader Load(string path, string without)
        {
            var shader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
            if (shader != null) return UnityEngine.Object.Instantiate(shader);
            if (s_Missing.Add(path + without)) Debug.LogError($"Unbound: the Visual Effect Graph package's {path} was not found; {without}");
            return null;
        }
    }

    /// <summary>
    /// The arguments of indirect draws and dispatches of effects, computed on the GPU from
    /// counts the kernels keep there, as the package's own kernels compute them: a user reserves
    /// an entry of a kind (a primitive, a mesh, or a dispatch), and before each use has the count
    /// copied in from the buffer its kernels count into (<see cref="Fill"/>), which computes the
    /// entry's ten words (Shaders/VFXFillIndirectArgs.compute: the counts of a draw or the thread
    /// groups of a dispatch, then the same for two instanced views). Entries are kept in one
    /// buffer for every effect; an entry's words start at <see cref="Offset"/>.
    /// </summary>
    internal sealed class VFXIndirectArgs
    {
        /// <summary>What an entry's arguments are for, as the package's kernel numbers them.</summary>
        internal enum Kind : uint
        {
            Points = 0, Lines = 1, Quads = 2, Hexahedrons = 3, Meshes = 4, Triangles = 5, Octagons = 6,
            Dispatch = 0xffffffff,
        }

        /// <summary>The words of an entry: five arguments, then five for two instanced views.</summary>
        internal const int WordsPerEntry = 10;
        /// <summary>The words describing an entry to the kernel: its kind, then a mesh's index
        /// count, first index and base vertex.</summary>
        const int DescriptionWords = 4;
        const int FirstCapacity = 16;
        /// <summary>The size of an element of a sorted list: a key and a particle's index.</summary>
        internal const int KeyIndexPairBytes = 8;

        static readonly int s_MaxNb = Shader.PropertyToID("maxNb");
        static readonly int s_Offset = Shader.PropertyToID("offset");
        static readonly int s_InputGpuBuffer = Shader.PropertyToID("inputGpuBuffer");
        static readonly int s_InputCpuBuffer = Shader.PropertyToID("inputCpuBuffer");
        static readonly int s_OutputIndirectArgs = Shader.PropertyToID("outputIndirectArgs");
        static readonly int s_DstOffset = Shader.PropertyToID("dstOffset");
        static readonly int s_Size = Shader.PropertyToID("size");
        static readonly int s_DispatchWidth = Shader.PropertyToID("dispatchWidth");
        static readonly int s_DstBuffer = Shader.PropertyToID("dstBuffer");
        static readonly int s_SrcUint = Shader.PropertyToID("srcStructBufferUint");
        static readonly int s_SrcKvp = Shader.PropertyToID("srcStructBufferKvp");

        /// <summary>Integers as the kernels' values hold them, boxed once: the word counts of a
        /// count (one, or two for a key and an index).</summary>
        static readonly object s_Zero = 0, s_One = 1, s_Two = 2;

        static VFXIndirectArgs s_Shared;

        /// <summary>The entries of every effect of the session.</summary>
        internal static VFXIndirectArgs Shared => s_Shared ??= new VFXIndirectArgs();

        readonly Stack<int> m_Free = new Stack<int>();
        uint[] m_Descriptions = new uint[FirstCapacity * DescriptionWords];
        int m_Used, m_Capacity;
        bool m_DescriptionsChanged;
        GraphicsBuffer m_Args, m_DescriptionBuffer, m_Counts;
        /// <summary>The package's kernels: the arguments' fill, the count copies, and the
        /// buffer clear (a copy of its own, its values set once).</summary>
        ComputeShader m_Fill, m_Copy, m_Clear;
        int m_FillKernel = -1, m_CopyUint, m_CopyKvp, m_ZeroKernel;
        /// <summary>The integers 0 to the entries' capacity, boxed once: an entry's place and
        /// the count past it, set for each fill without a new box.</summary>
        object[] m_Boxed = Array.Empty<object>();
        /// <summary>Entries' buffers made anew in frame <see cref="m_RetiredFrame"/>: draws
        /// collected before kept their handles, so they are released in a later frame.</summary>
        readonly List<GraphicsBuffer> m_Retired = new List<GraphicsBuffer>();
        int m_RetiredFrame;
        bool m_Loaded, m_Usable;

        /// <summary>The buffer of every entry's words (made anew, its words lost, when entries
        /// outgrow it: take it after <see cref="Ready"/> or <see cref="Fill"/>).</summary>
        internal GraphicsBuffer Arguments => m_Args;

        /// <summary>How many times the entries' buffer was made: an entry filled under another
        /// generation has lost its words.</summary>
        internal int Generation { get; private set; }

        /// <summary>Where an entry's words start, in bytes.</summary>
        internal static int Offset(int entry) => entry * WordsPerEntry * sizeof(uint);

        /// <summary>A new entry for arguments of <paramref name="kind"/> (a mesh's with its
        /// index count, first index and base vertex).</summary>
        internal int Reserve(Kind kind, uint indexCount = 0, uint firstIndex = 0, uint baseVertex = 0)
        {
            int entry = m_Free.Count != 0 ? m_Free.Pop() : m_Used++;
            if (m_Descriptions.Length < m_Used * DescriptionWords) Array.Resize(ref m_Descriptions, m_Descriptions.Length * 2);
            int at = entry * DescriptionWords;
            m_Descriptions[at] = (uint)kind;
            m_Descriptions[at + 1] = indexCount;
            m_Descriptions[at + 2] = firstIndex;
            m_Descriptions[at + 3] = baseVertex;
            m_DescriptionsChanged = true;
            return entry;
        }

        /// <summary>Gives an entry back.</summary>
        internal void Release(int entry)
        {
            if (entry >= 0) m_Free.Push(entry);
        }

        bool Usable()
        {
            if (m_Loaded) return m_Usable;
            m_Loaded = true;
            const string Without = "effects drawn through an indirect buffer are not drawn, and systems spawned by GPU events spawn nothing";
            m_Fill = VFXPackageShaders.Load(VFXPackageShaders.FillIndirectArgs, Without);
            m_Copy = VFXPackageShaders.Load(VFXPackageShaders.CopyBuffer, Without);
            m_Clear = VFXPackageShaders.Load(VFXPackageShaders.CopyBuffer, Without);
            m_Usable = m_Fill != null && m_Copy != null && m_Clear != null;
            if (!m_Usable) return false;
            m_FillKernel = m_Fill.FindKernel("CSVFXIndirectArgs");
            m_CopyUint = m_Copy.FindKernel("CSVFXBatchCopyCountUint");
            m_CopyKvp = m_Copy.FindKernel("CSVFXBatchCopyCountKvp");
            // One count at a time; a count is the first element of a list.
            m_Copy.SetBoxed(s_Size, s_One);
            m_ZeroKernel = m_Clear.FindKernel("CSVFXZeroInitBuffer");
            m_Clear.SetBoxed(s_DstOffset, s_Zero);
            m_Clear.SetBoxed(s_DispatchWidth, s_One);
            return true;
        }

        /// <summary>Whether entries can be filled now (the package's kernels are there), with a
        /// buffer for every entry reserved so far: an output takes the entries' buffer only
        /// after this, so that no entry reserved later makes it anew under a draw collected
        /// before.</summary>
        internal bool Ready()
        {
            if (!Usable()) return false;
            Prepare();
            return true;
        }

        /// <summary>Buffers for every reserved entry; the descriptions uploaded when they changed.</summary>
        void Prepare()
        {
            int frame = Time.s_FrameCount;
            if (m_Retired.Count != 0 && frame != m_RetiredFrame)
            {
                foreach (var buffer in m_Retired) buffer.Release();
                m_Retired.Clear();
            }
            if (m_Used > m_Capacity)
            {
                m_Capacity = Math.Max(FirstCapacity, m_Capacity);
                while (m_Capacity < m_Used) m_Capacity *= 2;
                if (m_Args != null)
                {
                    m_Retired.Add(m_Args);
                    m_RetiredFrame = frame;
                }
                m_DescriptionBuffer?.Release();
                m_Counts?.Release();
                Generation++;
                m_Args = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments, m_Capacity * WordsPerEntry, sizeof(uint));
                // The first instance of each set of arguments is zero, and stays so.
                m_Args.SetData(new uint[m_Capacity * WordsPerEntry]);
                m_DescriptionBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_Capacity, DescriptionWords * sizeof(uint));
                m_Counts = new GraphicsBuffer(GraphicsBuffer.Target.Raw, m_Capacity, sizeof(uint));
                if (m_Descriptions.Length < m_Capacity * DescriptionWords) Array.Resize(ref m_Descriptions, m_Capacity * DescriptionWords);
                m_Boxed = new object[m_Capacity + 1];
                for (int i = 0; i < m_Boxed.Length; i++) m_Boxed[i] = i;
                m_Fill.SetBuffer(m_FillKernel, s_InputGpuBuffer, m_Counts);
                m_Fill.SetBuffer(m_FillKernel, s_InputCpuBuffer, m_DescriptionBuffer);
                m_Fill.SetBuffer(m_FillKernel, s_OutputIndirectArgs, m_Args);
                m_Copy.SetBuffer(m_CopyUint, s_DstBuffer, m_Counts);
                m_Copy.SetBuffer(m_CopyKvp, s_DstBuffer, m_Counts);
                m_DescriptionsChanged = true;
            }
            if (m_DescriptionsChanged)
            {
                m_DescriptionsChanged = false;
                m_DescriptionBuffer.SetData(m_Descriptions, 0, 0, m_Capacity * DescriptionWords);
            }
        }

        /// <summary>Computes an entry's arguments from the count its kernels keep in the first
        /// element of <paramref name="counts"/> (a batch of one instance: the count of a list of
        /// indices, or of key and index pairs when <paramref name="keyIndexPairs"/>, whose index
        /// field counts). False when the package's kernels are missing (said once).</summary>
        internal bool Fill(int entry, GraphicsBuffer counts, bool keyIndexPairs)
        {
            if (!Usable()) return false;
            Prepare();
            int kernel = keyIndexPairs ? m_CopyKvp : m_CopyUint;
            m_Copy.SetBuffer(kernel, keyIndexPairs ? s_SrcKvp : s_SrcUint, counts);
            m_Copy.SetBoxed(s_DstOffset, m_Boxed[entry]);
            m_Copy.Dispatch(kernel, 1, 1, 1);
            m_Fill.SetBoxed(s_MaxNb, m_Boxed[entry + 1]);
            m_Fill.SetBoxed(s_Offset, m_Boxed[entry]);
            m_Fill.Dispatch(m_FillKernel, 1, 1, 1);
            return true;
        }

        /// <summary>Zeroes the count the kernels keep in the first element of
        /// <paramref name="counts"/> (the whole element: the key of a pair too).</summary>
        internal void ResetCount(GraphicsBuffer counts)
        {
            if (!Usable()) return;
            m_Clear.SetBuffer(m_ZeroKernel, s_DstBuffer, counts);
            m_Clear.SetBoxed(s_Size, counts.stride == KeyIndexPairBytes ? s_Two : s_One);
            m_Clear.Dispatch(m_ZeroKernel, 1, 1, 1);
        }
    }
}
