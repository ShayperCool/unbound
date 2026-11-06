using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    /// <summary>Graphics fences on Unbound's one GPU queue: the frame each fence's command ran in
    /// (-1 until it runs; one int per fence ever made). A CPU synchronisation fence has passed
    /// from the frame after it ran on: the Editor answers false in the frame its buffer runs, even
    /// once the GPU has finished it, true three frames later and never for a fence that never runs
    /// (measured, OpenGL Core: cmdbuffers fence.cpu.*, fence.frames.cpu.*; one or two frames
    /// later is unmeasured). Waits are no-ops: nothing runs on another queue.</summary>
    internal static class GraphicsFences
    {
        static readonly List<int> s_Ran = new List<int> { -1 };

        internal static GraphicsFence Create(GraphicsFenceType type)
        {
            s_Ran.Add(-1);
            return new GraphicsFence { m_Ptr = (IntPtr)(s_Ran.Count - 1), m_Version = 1, m_FenceType = type };
        }

        internal static void Ran(GraphicsFence fence) => s_Ran[(int)fence.m_Ptr] = Time.frameCount;

        internal static bool Passed(GraphicsFence fence)
        {
            int ran = s_Ran[(int)fence.m_Ptr];
            return ran >= 0 && Time.frameCount > ran;
        }

        /// <summary>The Editor's checks of a fence waited on (measured: fence.wait.default, fence.cpu.wait).</summary>
        internal static void CheckWait(GraphicsFence fence)
        {
            fence.Validate();
            if (fence.m_FenceType != GraphicsFenceType.AsyncQueueSynchronisation)
                throw new ArgumentException("Attempting to call WaitOnAsyncGPUFence on a fence that is not of GraphicsFenceType.AsyncQueueSynchronization");
        }
    }
}

namespace UnityEngine.Rendering
{
    public enum SynchronisationStage
    {
        VertexProcessing = 0,
        PixelProcessing = 1,
    }

    public partial struct GraphicsFence
    {
        internal IntPtr m_Ptr;
        internal int m_Version;
        internal GraphicsFenceType m_FenceType;

        /// <summary>Whether the GPU has passed the fence: a CPU synchronisation fence from the frame
        /// after its command ran (see GraphicsFences). Without async compute an async queue
        /// fence cannot answer (the Editor's exception, measured: fence.recorded, fence.frames);
        /// a fence not made by CreateGraphicsFence throws (fence.default).</summary>
        public bool passed
        {
            get
            {
                Validate();
                if (m_FenceType == GraphicsFenceType.AsyncQueueSynchronisation && !SystemInfo.supportsAsyncCompute)
                    throw new NotSupportedException("Cannot determine if this AsyncQueueSynchronisation GraphicsFence has passed as this platform does not support async compute.");
                return GraphicsFences.Passed(this);
            }
        }

        internal void Validate()
        {
            if (m_Ptr == IntPtr.Zero || m_Version == 0)
                throw new InvalidOperationException("This GraphicsFence object has not been correctly constructed see Graphics.CreateGraphicsFence() or CommandBuffer.CreateGraphicsFence()");
        }
    }

    public partial class CommandBuffer
    {
        /// <summary>A fence that passes once this command has run (the stage is not needed: one queue).</summary>
        public GraphicsFence CreateGraphicsFence(GraphicsFenceType fenceType, SynchronisationStageFlags stage)
        {
            var fence = GraphicsFences.Create(fenceType);
            Add(c => GraphicsFences.Ran(fence));
            return fence;
        }

        public GraphicsFence CreateAsyncGraphicsFence() => CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation, SynchronisationStageFlags.PixelProcessing);
        public GraphicsFence CreateAsyncGraphicsFence(SynchronisationStage stage) => CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation, SynchronisationStageFlags.PixelProcessing);

        /// <summary>Checked when recorded; the GPU has nothing to wait for on one queue.</summary>
        public void WaitOnAsyncGraphicsFence(GraphicsFence fence) => GraphicsFences.CheckWait(fence);
        public void WaitOnAsyncGraphicsFence(GraphicsFence fence, SynchronisationStage stage) => GraphicsFences.CheckWait(fence);
        public void WaitOnAsyncGraphicsFence(GraphicsFence fence, SynchronisationStageFlags stage) => GraphicsFences.CheckWait(fence);
    }
}

namespace UnityEngine
{
    public partial class Graphics
    {
        /// <summary>A fence after the work issued so far: it runs now.</summary>
        public static GraphicsFence CreateGraphicsFence(GraphicsFenceType fenceType, SynchronisationStageFlags stage)
        {
            var fence = GraphicsFences.Create(fenceType);
            GraphicsFences.Ran(fence);
            return fence;
        }

        public static GraphicsFence CreateAsyncGraphicsFence(SynchronisationStage stage) => CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation, SynchronisationStageFlags.PixelProcessing);
        public static GraphicsFence CreateAsyncGraphicsFence() => CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation, SynchronisationStageFlags.PixelProcessing);
        public static void WaitOnAsyncGraphicsFence(GraphicsFence fence) => GraphicsFences.CheckWait(fence);
        public static void WaitOnAsyncGraphicsFence(GraphicsFence fence, SynchronisationStage stage) => GraphicsFences.CheckWait(fence);
    }

    /// <summary>A GraphicsBuffer's identity on the GPU (<see cref="GraphicsBuffer.bufferHandle"/>):
    /// its render handle.</summary>
    public readonly partial struct GraphicsBufferHandle : IEquatable<GraphicsBufferHandle>
    {
        public readonly uint value;

        internal GraphicsBufferHandle(uint value) { this.value = value; }

        public override int GetHashCode() => value.GetHashCode();
        public override bool Equals(object obj) => obj is GraphicsBufferHandle other && other.value == value;
        public bool Equals(GraphicsBufferHandle other) => other.value == value;
        public int CompareTo(GraphicsBufferHandle other) => value.CompareTo(other.value);
        public static bool operator ==(GraphicsBufferHandle a, GraphicsBufferHandle b) => a.value == b.value;
        public static bool operator !=(GraphicsBufferHandle a, GraphicsBufferHandle b) => a.value != b.value;
    }

    public sealed partial class GraphicsBuffer
    {
        public GraphicsBufferHandle bufferHandle => new GraphicsBufferHandle((uint)Handle);
    }
}
