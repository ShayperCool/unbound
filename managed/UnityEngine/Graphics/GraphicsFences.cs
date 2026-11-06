using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    /// <summary>Fences share a state across struct copies and command-buffer replays. GPU
    /// completion comes from the queue; CPU visibility is delayed until two frames later as measured
    /// on the 6000.0.71f1 OpenGLCore Editor.</summary>
    internal static class GraphicsFences
    {
        internal sealed class State
        {
            internal int Frame = -1;
            internal ulong Sequence;
        }

        internal static GraphicsFence Create(GraphicsFenceType type) =>
            new GraphicsFence { m_State = new State(), m_FenceType = type };

        internal static void Ran(GraphicsFence fence)
        {
            if (fence.m_FenceType == GraphicsFenceType.CPUSynchronisation)
            {
                GfxStream.Flush();
                fence.m_State.Sequence = ulong.Parse(Native.Call("graphics-fence", "insert"), System.Globalization.CultureInfo.InvariantCulture);
            }
            fence.m_State.Frame = Time.frameCount;
        }

        internal static bool Passed(GraphicsFence fence) =>
            fence.m_State.Frame >= 0 && Time.frameCount - fence.m_State.Frame >= 2
            && ulong.Parse(Native.Call("graphics-fence", "poll"), System.Globalization.CultureInfo.InvariantCulture) >= fence.m_State.Sequence;

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
        internal GraphicsFences.State m_State;
        internal GraphicsFenceType m_FenceType;

        /// <summary>Whether the GPU has passed the fence: a CPU synchronisation fence from two frames
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
            if (m_State == null)
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
