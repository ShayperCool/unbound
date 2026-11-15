using System;
using System.Collections.Generic;
using UnityEditor.VFX;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.VFX;

namespace Unbound.Runtime
{
    /// <summary>
    /// A particle system of an effect: the particles live in GPU buffers and are spawned,
    /// updated and reaped by the compute kernels the graph compiled to. Each step the system
    /// takes the whole particles its spawners counted, uploads what the kernels read (the
    /// system's graph values, the spawn events and their attributes) and dispatches the
    /// initialize kernel for the new particles, then each update kernel.
    ///
    /// The kernels are written for batches of instances of one asset; an effect here is a batch
    /// of one instance, so its buffers hold one instance's sections and the indirection buffers
    /// name instance zero. What scripts observe of a system (how many particles it took and has
    /// alive, whether it sleeps) matches the Unity 6000.0.71f1 Editor (tests/projects/vfx).
    /// </summary>
    internal sealed class VFXParticleSystem : VFXSystem
    {
        /// <summary>The thread group size of every kernel the graph compiler writes.</summary>
        const int ThreadsPerGroup = 64;
        /// <summary>The most thread groups one dimension of a dispatch takes.</summary>
        const uint MaxGroupsPerDimension = 0xffff;
        /// <summary>The words that start an instance's graph values: the most particles a kernel
        /// visits, the system's seed, the spawn index of the step's first particle and padding.</summary>
        const int ContextDataWords = 4;
        /// <summary>The dead list starts with the instance's dead count and its copy for the
        /// initialize kernel; the dead particle indices follow.</summary>
        const int DeadListHeaderWords = 2;
        /// <summary>How many frames pass between two requests for the alive count.</summary>
        const int ReadbackPeriod = 60;

        internal readonly uint Capacity;
        internal readonly bool HasKill, HasStrips, ReceivesGpuEvents;

        readonly int m_AttributeBuffer, m_SourceBuffer, m_DeadList, m_Indirection, m_GraphValuesBuffer, m_PrefixSum, m_SpawnBuffer;
        readonly int[] m_GraphValueExpressions, m_GraphValueOffsets;
        readonly int m_BoundsCenter, m_BoundsSize, m_BoundsPadding;
        readonly VFXSpawnerSystem[] m_Spawners;
        readonly Kernel m_Initialize;
        readonly Kernel[] m_Updates;
        readonly VFXOutput[] m_Outputs;
        /// <summary>Whether a step uploaded what the outputs' programs read.</summary>
        bool m_Stepped;
        /// <summary>Per source attribute: where an event has it (a word of the event attributes)
        /// and where the kernels read it (a word of an event's structure in the source buffer).</summary>
        readonly (int From, int To, int Structure, int Words)[] m_SourceLayout;

        // The step's spawn events: how many particles each asks for (as prefix sums) and their
        // attributes, in the source buffer's layout.
        readonly List<uint> m_SpawnPrefix = new List<uint>();
        uint[] m_SourceWords = new uint[4];
        uint[] m_SpawnWords = new uint[4];
        readonly uint[] m_GraphWords;
        readonly uint[] m_PrefixWords = new uint[2];
        uint m_Spawned, m_Total;
        uint m_LastActiveFrame;
        bool m_Sleeping = true;

        // The alive count of a system that kills particles is read back from the GPU.
        int m_ReadAlive = -1;
        uint m_NextReadbackFrame;
        bool m_ReadbackPending;
        uint m_ReadbackFrame;
        int m_ReadbackGeneration, m_RequestGeneration;
        Action<AsyncGPUReadbackRequest> m_OnReadback;

        internal VFXParticleSystem(VisualEffect effect, int index, IReadOnlyList<VFXSystem> systems) : base(effect, index)
        {
            var data = effect.Data;
            Capacity = Desc.capacity;
            HasKill = (Desc.flags & VFXSystemFlag.SystemHasKill) != 0;
            HasStrips = (Desc.flags & VFXSystemFlag.SystemHasStrips) != 0;
            ReceivesGpuEvents = (Desc.flags & VFXSystemFlag.SystemReceivedEventGPU) != 0;
            m_AttributeBuffer = Value(Desc.buffers, "attributeBuffer");
            m_SourceBuffer = Value(Desc.buffers, "sourceAttributeBuffer");
            m_DeadList = Value(Desc.buffers, "deadList");
            m_Indirection = Value(Desc.buffers, "instancingIndirectAndActiveIndirect");
            m_GraphValuesBuffer = Value(Desc.buffers, "graphValuesBuffer");
            m_PrefixSum = Value(Desc.buffers, "instancingPrefixSum");
            m_SpawnBuffer = Value(Desc.buffers, "spawnBuffer");
            m_BoundsCenter = Value(Desc.values, "bounds_center");
            m_BoundsSize = Value(Desc.values, "bounds_size");
            m_BoundsPadding = Value(Desc.values, "boundsPadding");

            // The graph values: the system's values from the marker on, in the compiler's order.
            var values = Desc.values ?? Array.Empty<VFXMapping>();
            int marker = Find(values, "graphValuesOffset");
            int first = marker < 0 ? values.Length : values[marker].index;
            m_GraphValueExpressions = new int[Math.Max(0, values.Length - first)];
            for (int i = 0; i < m_GraphValueExpressions.Length; i++) m_GraphValueExpressions[i] = values[first + i].index;
            m_GraphValueOffsets = GraphValueOffsets(data, m_GraphValueExpressions, out int graphWords);
            m_GraphWords = new uint[graphWords];

            var spawners = new List<VFXSpawnerSystem>();
            foreach (var mapping in Desc.buffers ?? Array.Empty<VFXMapping>())
            {
                if (mapping.name != "spawner_input") continue;
                foreach (var system in systems)
                    if (system is VFXSpawnerSystem spawner && spawner.OutputBufferIndex == mapping.index) spawners.Add(spawner);
            }
            m_Spawners = spawners.ToArray();
            m_SourceLayout = SourceLayout(data, m_SourceBuffer);

            var updates = new List<Kernel>();
            var outputs = new List<VFXOutput>();
            var processors = index < data.Processors.Length ? data.Processors[index] : Array.Empty<UnityEngine.Object>();
            for (int i = 0; i < (Desc.tasks?.Length ?? 0); i++)
            {
                var task = Desc.tasks[i];
                var processor = i < processors.Length ? processors[i] : null;
                if (VFXOutput.IsOutput(task.type))
                {
                    string sourceRefusal = index < data.OutputRefusals.Length && i < data.OutputRefusals[index].Length ? data.OutputRefusals[index][i] : null;
                    string refusal = VFXOutput.Refusal(Desc, task, processor, sourceRefusal);
                    if (refusal == null) outputs.Add(new VFXOutput(this, task, (Material)processor));
                    else effect.WarnOutputNotDrawn(Desc.name, refusal);
                    continue;
                }
                if (!(processor is ComputeShader shader)) continue;
                if (task.type == VFXTaskType.Initialize) m_Initialize = new Kernel(this, task, shader);
                else if (task.type == VFXTaskType.Update) updates.Add(new Kernel(this, task, shader));
            }
            m_Updates = updates.ToArray();
            m_Outputs = outputs.ToArray();
        }

        /// <summary>How an event's attributes map into the source buffer: by name, from the
        /// event attribute layout every spawner shares to the layout the kernels read.</summary>
        static (int, int, int, int)[] SourceLayout(VisualEffectCompiledData data, int sourceBuffer)
        {
            var target = sourceBuffer >= 0 && sourceBuffer < data.GpuBuffers.Length ? data.GpuBuffers[sourceBuffer].layout : null;
            var source = data.CpuBuffers.Length > 0 ? data.CpuBuffers[0].layout : null;
            if (target == null || source == null) return Array.Empty<(int, int, int, int)>();
            var layout = new List<(int, int, int, int)>();
            foreach (var to in target)
                foreach (var from in source)
                    if (from.name == to.name)
                        layout.Add(((int)(from.offset.bucket + from.offset.element), (int)(to.offset.bucket + to.offset.element),
                            (int)to.offset.structure, VFXExpressionHelper.GetSizeOfType(to.type)));
            return layout.ToArray();
        }

        /// <summary>Where each graph value starts in an instance's words, and the words in all:
        /// the context data, then each value in blocks of four words (a value that does not fit
        /// the block starts the next), padded to whole blocks.</summary>
        internal static int[] GraphValueOffsets(VisualEffectCompiledData data, int[] expressions, out int words)
        {
            var offsets = new int[expressions.Length];
            int at = ContextDataWords, block = 0;
            for (int i = 0; i < expressions.Length; i++)
            {
                int size = VFXExpressionHelper.GetSizeOfType(data.ExpressionTypes[expressions[i]]);
                if (block + size > 4) { at += (4 - block % 4) % 4; block = 0; }
                offsets[i] = at;
                at += size;
                block += size;
            }
            words = at + (4 - block % 4) % 4;
            return offsets;
        }

        void PackGraphValues(uint maxParticles, uint seed, uint spawnIndex)
        {
            var data = Effect.Data;
            var words = Effect.Values.Words;
            var into = m_GraphWords;
            into[0] = maxParticles;
            into[1] = seed;
            into[2] = spawnIndex;
            for (int i = 0; i < m_GraphValueExpressions.Length; i++)
            {
                int expression = m_GraphValueExpressions[i];
                var type = data.ExpressionTypes[expression];
                int size = VFXExpressionHelper.GetSizeOfType(type), from = data.ValueOffsets[expression], at = m_GraphValueOffsets[i];
                // A boolean is its low byte.
                for (int k = 0; k < size; k++) into[at + k] = type == VFXValueType.Boolean ? words[from + k] & 0xff : words[from + k];
            }
        }

        // ------------------------------------------------------------------------------ state

        /// <summary>The system's particles as the effect counts them: the particles spawned so
        /// far when none dies, else the count last read back from the GPU (-1 before the first
        /// answer arrives).</summary>
        internal int AliveCount => HasKill ? m_ReadAlive : (int)m_Total;

        internal bool Sleeping => m_Sleeping;

        /// <summary>The system's bounds in the effect's space: the box the graph gives it, grown
        /// by its padding (a system in world space has its box brought into the effect's space).
        /// False when the graph gives the system no box.</summary>
        internal bool LocalBounds(VisualEffectState state, out Bounds bounds)
        {
            bounds = default;
            if (m_BoundsCenter < 0 || m_BoundsSize < 0) return false;
            var values = Effect.Values;
            Vector3 center = values.GetVector(m_BoundsCenter, 3), size = values.GetVector(m_BoundsSize, 3);
            if (m_BoundsPadding >= 0) size += 2f * (Vector3)values.GetVector(m_BoundsPadding, 3);
            bounds = new Bounds(center, size);
            if ((Desc.flags & VFXSystemFlag.SystemInWorldSpace) != 0) bounds = UnityEngine.Renderer.TransformBounds(bounds, state.WorldToLocal);
            return true;
        }

        internal override void Reset()
        {
            m_SpawnPrefix.Clear();
            m_Spawned = m_Total = 0;
            m_LastActiveFrame = 0;
            m_Sleeping = true;
            m_ReadAlive = -1;
            m_ReadbackPending = false;
            m_ReadbackGeneration++;
            m_NextReadbackFrame = VFXManager.Frame + 1;
            m_PrefixWords[0] = m_PrefixWords[1] = 0;
            if (Capacity == 0) return;

            // Every particle dead: the list holds the capacity as its count and the indices so
            // that the first particle spawned takes index zero. The attributes start as zeros
            // (no particle alive).
            if (m_DeadList >= 0)
            {
                if (m_NewDeadList == null)
                {
                    m_NewDeadList = new uint[DeadListHeaderWords + Capacity];
                    m_NewDeadList[0] = m_NewDeadList[1] = Capacity;
                    for (uint i = 0; i < Capacity; i++) m_NewDeadList[DeadListHeaderWords + i] = Capacity - i - 1;
                }
                Effect.GpuBuffer(m_DeadList, m_NewDeadList.Length).SetData(m_NewDeadList);
            }
            if (m_AttributeBuffer >= 0)
            {
                var buffer = Effect.GpuBuffer(m_AttributeBuffer, 1);
                buffer.SetData(m_NoAttributes ??= new uint[buffer.count]);
            }
            if (m_Indirection >= 0) Effect.GpuBuffer(m_Indirection, 2).SetData(m_PrefixWords, 0, 0, 2);
        }

        /// <summary>What a reset uploads, made once: a dead list of every particle, and
        /// attributes of none.</summary>
        uint[] m_NewDeadList, m_NoAttributes;

        /// <summary>Takes a spawn event: the whole particles it asks for, as far as the system
        /// has room (a system whose particles never die fills up for good).</summary>
        void TakeEvent(uint[] attributes)
        {
            float asked = attributes.Length > 0 ? BitConverter.UInt32BitsToSingle(attributes[0]) : 0f;
            uint room = Capacity - m_Spawned;
            if (!HasKill) room -= Math.Min(room, m_Total);
            // The count as an unsigned value: a count of minus one or less asks for all the room.
            uint count = Math.Min(VFXExpressionEvaluator.ToUint(asked), room);
            if (count == 0) return;

            int eventIndex = m_SpawnPrefix.Count;
            foreach (var (from, to, structure, size) in m_SourceLayout)
            {
                int at = eventIndex * structure + to;
                if (at + size > m_SourceWords.Length) Array.Resize(ref m_SourceWords, Math.Max(at + size, m_SourceWords.Length * 2));
                Array.Copy(attributes, from, m_SourceWords, at, size);
            }
            m_Spawned += count;
            m_SpawnPrefix.Add((eventIndex == 0 ? 0u : m_SpawnPrefix[eventIndex - 1]) + count);
        }

        void UpdateSleepState(uint frame)
        {
            if (m_Spawned != 0) m_LastActiveFrame = frame;
            bool wasSleeping = m_Sleeping;
            if (!HasKill) m_Sleeping = m_Total == 0;
            else if (m_LastActiveFrame < m_ReadbackFrame)
            {
                // An answer newer than the last spawn says whether anything is left alive; a
                // sleeping system stays asleep until it spawns.
                if (m_Sleeping) return;
                if (!m_ReadbackPending) m_Sleeping = m_ReadAlive == 0;
            }
            else m_Sleeping = false;
            if (wasSleeping && !m_Sleeping)
            {
                // Awake again: the old count says nothing, and a new one is asked for next frame.
                m_ReadAlive = -1;
                m_NextReadbackFrame = VFXManager.Frame + 1;
            }
        }

        /// <summary>Whether every kernel of the system compiles (checked once, before its
        /// first step): a system with one that does not is stopped, and says so. Without a
        /// renderer (a headless run, a test) no kernel runs, nothing is read back and nothing
        /// is said: the system goes on with what it counts itself. Whether there is a renderer
        /// is asked then too, and that answer is final for the system.</summary>
        bool KernelsUsable()
        {
            if (m_KernelsChecked) return m_KernelsUsable;
            m_KernelsChecked = true;
            m_HasRenderer = GraphicsFences.RendererPresent;
            m_KernelsUsable = true;
            if (!m_HasRenderer) return true;
            if (m_Initialize != null) m_KernelsUsable = m_Initialize.Usable();
            foreach (var update in m_Updates) m_KernelsUsable &= update.Usable();
            return m_KernelsUsable;
        }

        bool m_KernelsChecked, m_KernelsUsable, m_HasRenderer;

        internal override void Update(VisualEffectState state)
        {
            if (!KernelsUsable()) return;
            foreach (var spawner in m_Spawners) TakeEvent(spawner.State.Attribute.Words);
            m_Total += m_Spawned;
            UpdateSleepState(state.FrameIndex);
            if (m_Sleeping || Capacity == 0)
            {
                m_Spawned = 0;
                m_SpawnPrefix.Clear();
                return;
            }

            uint spawned = m_Spawned;
            uint spawnIndex = m_Total - spawned;
            // A system that neither kills nor takes GPU events visits the particles spawned so
            // far; any other visits its whole capacity.
            uint maxParticles = !HasKill && !ReceivesGpuEvents && !HasStrips ? m_Total : Capacity;
            PackGraphValues(maxParticles, state.Seed, spawnIndex);
            if (m_GraphValuesBuffer >= 0) Effect.GpuBuffer(m_GraphValuesBuffer, m_GraphWords.Length).SetData(m_GraphWords, 0, 0, m_GraphWords.Length);
            m_Stepped = true;

            // The update kernels of a system without a fixed size per instance find their
            // instance in the second prefix sum; the initialize kernel reads the first.
            m_PrefixWords[0] = spawned;
            m_PrefixWords[1] = maxParticles;
            if (m_PrefixSum >= 0) Effect.GpuBuffer(m_PrefixSum, 2).SetData(m_PrefixWords, 0, 0, 2);

            if (spawned != 0 && m_Initialize != null)
            {
                int events = m_SpawnPrefix.Count;
                if (events + 1 > m_SpawnWords.Length) Array.Resize(ref m_SpawnWords, Math.Max(events + 1, m_SpawnWords.Length * 2));
                // One active instance: its event count, then each event's particles so far.
                m_SpawnWords[0] = (uint)events;
                for (int i = 0; i < events; i++) m_SpawnWords[1 + i] = m_SpawnPrefix[i];
                if (m_SpawnBuffer >= 0) Effect.GpuBuffer(m_SpawnBuffer, events + 1).SetData(m_SpawnWords, 0, 0, events + 1);
                if (m_SourceBuffer >= 0 && m_SourceLayout.Length > 0)
                {
                    int words = SourceWords(events);
                    Effect.GpuBuffer(m_SourceBuffer, words).SetData(m_SourceWords, 0, 0, words);
                }
                // The initialize kernel takes particles from the dead list as long as the count
                // it had when the step began allows: the count's copy.
                if (HasKill && !HasStrips && m_DeadList >= 0) VFXManager.CopyDeadCount(Effect.GpuBuffer(m_DeadList, 0));
                m_Initialize.Dispatch(spawned, 0);
            }
            m_Spawned = 0;
            m_SpawnPrefix.Clear();

            bool fixedSize = HasKill || HasStrips;
            uint updated = fixedSize ? Math.Max(AlignThreadGroups(Capacity), ThreadsPerGroup) : maxParticles;
            foreach (var update in m_Updates) update.Dispatch(updated, fixedSize ? 0 : 1);
        }

        /// <summary>The particles an output draws: the ones spawned so far of a system that
        /// neither kills nor takes GPU events, else every particle it has room for (the vertex
        /// programs leave out the dead ones). None before the system's first step, nor while
        /// it sleeps (all its particles are dead).</summary>
        internal uint DrawnParticles => !m_Stepped || m_Sleeping ? 0 : !HasKill && !ReceivesGpuEvents ? Math.Min(m_Total, Capacity) : Capacity;

        /// <summary>Adds the draws of the system's outputs, in the graph's order.</summary>
        internal void CollectDraws(Renderer renderer, FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            foreach (var output in m_Outputs) output.Collect(renderer, camera, into);
        }

        /// <summary>The words of the source buffer the step's events reach.</summary>
        int SourceWords(int events)
        {
            int words = 0;
            foreach (var (_, to, structure, size) in m_SourceLayout) words = Math.Max(words, (events - 1) * structure + to + size);
            return Math.Min(words, m_SourceWords.Length);
        }

        /// <summary>After the frame's steps: asks the GPU for the alive count of a system that
        /// kills particles, once a period, and takes an answer that arrived.</summary>
        internal void UpdateReadback()
        {
            if (!HasKill || HasStrips || m_DeadList < 0 || Capacity == 0 || !m_HasRenderer) return;
            uint frame = VFXManager.Frame;
            if (m_ReadbackPending || m_NextReadbackFrame > frame || m_Sleeping) return;
            m_ReadbackPending = true;
            m_ReadbackFrame = frame;
            m_RequestGeneration = m_ReadbackGeneration;
            AsyncGPUReadback.Request(Effect.GpuBuffer(m_DeadList, 0), sizeof(uint), 0, m_OnReadback ??= OnReadback);
        }

        void OnReadback(AsyncGPUReadbackRequest request)
        {
            // A system reset since it asked takes no answer from before.
            if (m_RequestGeneration != m_ReadbackGeneration) return;
            m_ReadbackPending = false;
            m_NextReadbackFrame += ReadbackPeriod;
            if (request.hasError) return;
            m_ReadAlive = (int)(Capacity - Math.Min(Capacity, request.GetData<uint>()[0]));
        }

        /// <summary>A count rounded up to whole thread groups.</summary>
        static uint AlignThreadGroups(uint count) => (count + ThreadsPerGroup - 1) / ThreadsPerGroup * ThreadsPerGroup;

        /// <summary>The thread groups of a dispatch over <paramref name="count"/> threads: as
        /// many rows as one dimension's limit asks for, each of the same width.</summary>
        static void ThreadGroups(uint count, out int x, out int y)
        {
            uint groups = (count + ThreadsPerGroup - 1) / ThreadsPerGroup;
            uint rows = (groups + MaxGroupsPerDimension - 1) / MaxGroupsPerDimension;
            y = (int)rows;
            x = rows == 0 ? 0 : (int)((groups + rows - 1) / rows);
        }

        /// <summary>The values of a task that are objects: the textures and graphics buffers
        /// its kernel declares, by the names it declares them with (numbers reach a kernel
        /// through the graph values).</summary>
        internal static (int Name, int Expression)[] ObjectValues(VisualEffectCompiledData data, in VFXEditorTaskDesc task)
        {
            var objects = new List<(int, int)>();
            foreach (var value in task.values ?? Array.Empty<VFXMapping>())
            {
                if (value.index < 0 || value.index >= data.ExpressionTypes.Length) continue;
                var type = data.ExpressionTypes[value.index];
                if (VisualEffectCompiledData.IsTextureType(type) || type == VFXValueType.Buffer) objects.Add((Shader.PropertyToID(value.name), value.index));
            }
            return objects.ToArray();
        }

        /// <summary>A compute task of the system: its shader with the buffers it reads bound.</summary>
        sealed class Kernel
        {
            static readonly int s_DispatchWidth = Shader.PropertyToID("dispatchWidth");
            static readonly int s_InstancingConstants = Shader.PropertyToID("instancingConstants");
            static readonly int s_InstancingBufferOffsets = Shader.PropertyToID("instancingBufferOffsets");

            readonly VFXParticleSystem m_System;
            readonly VFXEditorTaskDesc m_Task;
            readonly ComputeShader m_Shader;
            readonly int m_Kernel;
            readonly int[] m_BufferNames;
            /// <summary>The buffers bound, to bind one again only once it was replaced.</summary>
            readonly GraphicsBuffer[] m_Bound;
            /// <summary>The textures and graphics buffers the kernel reads: values of the
            /// effect, bound by name (again only once the value is another object).</summary>
            readonly VFXTaskObjects m_Objects;
            int m_DispatchWidth = -1, m_PrefixOffset = -1;
            bool m_Checked, m_Failed;

            internal Kernel(VFXParticleSystem system, in VFXEditorTaskDesc task, ComputeShader shader)
            {
                m_System = system;
                m_Task = task;
                // The task's own copy: its bindings are this effect's.
                m_Shader = UnityEngine.Object.Instantiate(shader);
                m_Kernel = m_Shader.FindKernel("CSMain");
                int count = task.buffers?.Length ?? 0;
                m_BufferNames = new int[count];
                m_Bound = new GraphicsBuffer[count];
                for (int i = 0; i < count; i++) m_BufferNames[i] = Shader.PropertyToID(task.buffers[i].name);
                m_Objects = new VFXTaskObjects(system.Effect.Data, task);
                // One instance, active, in a batch of one, at offset zero: the counts as the
                // kernels read them, the bits of unsigned integers.
                float one = BitConverter.Int32BitsToSingle(1);
                m_Shader.SetVector(s_InstancingConstants, new Vector4(one, one, one, 0f));
            }

            /// <summary>Whether the kernel compiles here; said once when it does not (a system
            /// whose kernel cannot run stops, rather than count particles nothing spawns).</summary>
            internal bool Usable()
            {
                if (m_Checked) return !m_Failed;
                m_Checked = true;
                try
                {
                    m_Shader.GetKernelThreadGroupSizes(m_Kernel, out _, out _, out _);
                }
                catch (Exception e)
                {
                    m_Failed = true;
                    var effect = m_System.Effect;
                    Debug.LogError($"Unbound: VisualEffect '{effect.name}': the {m_Task.type} kernel of '{m_System.Desc.name}' does not compile ({e.Message}); the system does not simulate", effect);
                }
                return !m_Failed;
            }

            /// <summary>Runs the kernel over <paramref name="count"/> threads, the instance's
            /// particle counts at <paramref name="prefixOffset"/> of the prefix sums.</summary>
            internal void Dispatch(uint count, int prefixOffset)
            {
                if (count == 0 || m_Failed) return;
                var effect = m_System.Effect;
                var values = effect.Values;
                for (int i = 0; i < m_Objects.Slots.Length; i++)
                {
                    if (!m_Objects.Changed(values, i, out object value)) continue;
                    int name = m_Objects.Slots[i].Name;
                    if (value is Texture texture) m_Shader.SetTexture(m_Kernel, name, texture);
                    else if (value is GraphicsBuffer graphicsBuffer) m_Shader.SetBuffer(m_Kernel, name, graphicsBuffer);
                    // A property without an object leaves the kernel what it has without one.
                    else m_Shader.Unbind(m_Kernel, name);
                }
                for (int i = 0; i < m_BufferNames.Length; i++)
                {
                    int index = m_Task.buffers[i].index;
                    if (index < 0) continue;
                    var buffer = effect.GpuBuffer(index, 0);
                    if (ReferenceEquals(buffer, m_Bound[i])) continue;
                    m_Bound[i] = buffer;
                    m_Shader.SetBuffer(m_Kernel, m_BufferNames[i], buffer);
                }
                ThreadGroups(count, out int x, out int y);
                if (x != m_DispatchWidth) m_Shader.SetInt(s_DispatchWidth, m_DispatchWidth = x);
                if (prefixOffset != m_PrefixOffset) m_Shader.SetVector(s_InstancingBufferOffsets, new Vector4(0f, m_PrefixOffset = prefixOffset, 0f, 0f));
                m_Shader.Dispatch(m_Kernel, x, y, 1);
            }

            internal void Release() => UnityEngine.Object.Destroy(m_Shader);
        }

        internal override void Release()
        {
            m_ReadbackGeneration++;
            m_Initialize?.Release();
            foreach (var update in m_Updates) update.Release();
        }
    }
}
