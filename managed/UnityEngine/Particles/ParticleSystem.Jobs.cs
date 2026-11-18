using System;
using System.Runtime.InteropServices;
using Unbound.Runtime;
using Unbound.Runtime.Jobs;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine.ParticleSystemJobs;

namespace UnityEngine
{
    // The particle system's side of the particle job API (ParticleSystemJobs.cs): the particles a
    // job sees, as one float array per component, copied from the system when its message's first
    // job is scheduled and back when the system's update completes or a script uses the system.
    public sealed partial class ParticleSystem
    {
        ParticleJobBuffers m_JobBuffers;
        JobHandle m_ManagedJobHandle;
        /// <summary>A job runs, or ran, on this update's copy, which goes back to the particles.</summary>
        internal bool m_JobsPending;
        /// <summary>The frame's update, or a Simulate call, stepped the system: its job message is due.</summary>
        internal bool m_SteppedThisFrame, m_SteppedBySimulate;
        /// <summary>A script stopped the playing system since its last update.</summary>
        internal bool m_StoppedThisFrame;

        internal unsafe void* GetManagedJobData()
        {
            m_JobBuffers ??= new ParticleJobBuffers();
            if (!m_JobsPending)
            {
                m_JobBuffers.Fill(this);
                m_JobsPending = true;
                ParticleSystemManager.NoteJobs(this);
            }
            return m_JobBuffers.List;
        }

        internal JobHandle GetManagedJobHandle() => m_ManagedJobHandle;

        internal void SetManagedJobHandle(JobHandle handle) => m_ManagedJobHandle = handle;

        /// <summary>A job of one IJobParticleSystem: it gets the system's list as its additional data.</summary>
        internal static unsafe JobHandle ScheduleManagedJob(ref JobsUtility.JobScheduleParameters parameters, void* additionalData) =>
            JobSystem.Schedule(ref parameters, JobSystem.Kind.Single, 1, 1, additionalData: additionalData);

        internal static unsafe void CopyManagedJobData(void* systemPtr, out NativeParticleData particleData) =>
            particleData = *(NativeParticleData*)systemPtr;

        internal static bool UserJobCanBeScheduled() => ParticleSystemManager.SendingJobMessages;

        /// <summary>Waits for the system's jobs and takes their writes into the particles.</summary>
        internal void CompleteManagedJobs()
        {
            if (!m_JobsPending) return;
            m_JobsPending = false;
            m_ManagedJobHandle.Complete();
            m_ManagedJobHandle = default;
            if (!m_Destroyed) m_JobBuffers.Flush(this);
        }

        /// <summary>The system is gone: its jobs finish and their buffers go.</summary>
        internal void DisposeManagedJobs()
        {
            m_ManagedJobHandle.Complete();
            m_ManagedJobHandle = default;
            m_JobsPending = false;
            m_JobBuffers?.Dispose();
            m_JobBuffers = null;
        }
    }
}

namespace Unbound.Runtime
{
    using UnityEngine;
    using UnityEngine.ParticleSystemJobs;

    /// <summary>The particles of one system as particle jobs see them: a list header (the system
    /// pointer, the count and the capacity, an UnsafeList's layout for the deferred parallel-for),
    /// the arrays' pointers, and one array per component, all in one unmanaged block that grows
    /// with the particle count and is reused from update to update.</summary>
    internal sealed unsafe class ParticleJobBuffers : IDisposable
    {
        /// <summary>Four-byte slots per particle: positions, velocities, axes of rotation,
        /// rotations, rotational speeds and sizes (3 each), the start colour, the percentage of the
        /// lifetime lived, the inverse start lifetime, the random seed, two custom data streams (4
        /// each) and the mesh index.</summary>
        const int SlotsPerParticle = 6 * 3 + 1 + 1 + 1 + 1 + 2 * 4 + 1;

        NativeListData* m_List;
        NativeParticleData* m_Data;
        float* m_Slots;
        int m_Capacity;
        /// <summary>The arrays of the streams a system may not have (null in the job when it does not).</summary>
        NativeParticleData.Array4 m_Custom1, m_Custom2;
        void* m_MeshIndices;
        /// <summary>Per custom data stream: a job wrote it after this module run (see <see cref="HoldsCustom"/>).</summary>
        uint m_CustomRun0, m_CustomRun1;
        bool m_CustomHeld0, m_CustomHeld1;

        internal NativeListData* List => m_List;

        internal ParticleJobBuffers()
        {
            m_List = (NativeListData*)NativeMemory.AllocZeroed((nuint)sizeof(NativeListData));
            m_Data = (NativeParticleData*)NativeMemory.AllocZeroed((nuint)sizeof(NativeParticleData));
            m_List->system = m_Data;
        }

        ~ParticleJobBuffers() => Free();

        public void Dispose()
        {
            Free();
            GC.SuppressFinalize(this);
        }

        void Free()
        {
            if (m_Slots != null) NativeMemory.Free(m_Slots);
            if (m_Data != null) NativeMemory.Free(m_Data);
            if (m_List != null) NativeMemory.Free(m_List);
            m_Slots = null;
            m_Data = null;
            m_List = null;
        }

        void Reserve(int count)
        {
            if (count <= m_Capacity && m_Slots != null) return;
            int capacity = Math.Max(Math.Max(count, 16), m_Capacity * 2);
            if (m_Slots != null) NativeMemory.Free(m_Slots);
            m_Slots = (float*)NativeMemory.AllocZeroed((nuint)capacity * SlotsPerParticle, sizeof(float));
            m_Capacity = capacity;
            float* next = m_Slots;
            float* Take() { float* at = next; next += capacity; return at; }
            NativeParticleData.Array3 Take3() => new NativeParticleData.Array3 { x = Take(), y = Take(), z = Take() };
            NativeParticleData.Array4 Take4() => new NativeParticleData.Array4 { x = Take(), y = Take(), z = Take(), w = Take() };
            m_Data->positions = Take3();
            m_Data->velocities = Take3();
            m_Data->axisOfRotations = Take3();
            m_Data->rotations = Take3();
            m_Data->rotationalSpeeds = Take3();
            m_Data->sizes = Take3();
            m_Data->startColors = Take();
            m_Data->aliveTimePercent = Take();
            m_Data->inverseStartLifetimes = Take();
            m_Data->randomSeeds = Take();
            m_Custom1 = Take4();
            m_Custom2 = Take4();
            m_MeshIndices = Take();
        }

        /// <summary>A custom data stream a job wrote holds what it wrote until the module's next run
        /// over the particles (6000.0.71f1, tests/projects/particlejobs write.custom1).</summary>
        internal bool HoldsCustom(int stream, uint run) =>
            stream == 0 ? m_CustomHeld0 && m_CustomRun0 == run : m_CustomHeld1 && m_CustomRun1 == run;

        /// <summary>Whether the system has the stream: the custom data module fills it, or a script
        /// set it (the module's absent stream is a null array in the job: measured for Custom2 with
        /// the module on and the stream off).</summary>
        static bool HasCustom(ParticleSystem ps, int stream) =>
            ps.m_Custom[stream] != null || (ps.m_CustomData.enabled && ps.m_CustomData.modes[stream] != ParticleSystemCustomDataMode.Disabled);

        /// <summary>Mesh indices exist for a renderer drawing meshes (a billboard renderer's are a null
        /// array, measured; Mesh mode's array is Unbound's choice, unmeasured).</summary>
        static bool HasMeshIndices(ParticleSystem ps) =>
            ps.CullingRenderer is ParticleSystemRenderer renderer && renderer.renderMode == ParticleSystemRenderMode.Mesh;

        static void Put(ref NativeParticleData.Array3 a, int i, Vector3 v)
        {
            a.x[i] = v.x;
            a.y[i] = v.y;
            a.z[i] = v.z;
        }

        static Vector3 Get(ref NativeParticleData.Array3 a, int i) => new Vector3(a.x[i], a.y[i], a.z[i]);

        /// <summary>The system's particles into the arrays (main thread, before the jobs).</summary>
        internal void Fill(ParticleSystem ps)
        {
            int count = ps.m_Count;
            Reserve(count);
            m_Data->count = count;
            m_List->length = count;
            m_List->capacity = m_Capacity;
            var particles = ps.m_Particles;
            var colors = (Color32*)m_Data->startColors;
            var alive = (float*)m_Data->aliveTimePercent;
            var inverse = (float*)m_Data->inverseStartLifetimes;
            var seeds = (uint*)m_Data->randomSeeds;
            bool custom1 = HasCustom(ps, 0), custom2 = HasCustom(ps, 1), meshIndices = HasMeshIndices(ps);
            m_Data->customData1 = custom1 ? m_Custom1 : default;
            m_Data->customData2 = custom2 ? m_Custom2 : default;
            m_Data->meshIndices = meshIndices ? m_MeshIndices : null;
            var meshes = (int*)m_MeshIndices;
            for (int i = 0; i < count; i++)
            {
                ref var p = ref particles[i];
                Put(ref m_Data->positions, i, p.m_Position);
                Put(ref m_Data->velocities, i, p.m_Velocity);
                Put(ref m_Data->axisOfRotations, i, p.m_AxisOfRotation);
                Put(ref m_Data->rotations, i, p.m_Rotation);
                Put(ref m_Data->rotationalSpeeds, i, p.m_AngularVelocity);
                Put(ref m_Data->sizes, i, p.m_StartSize);
                colors[i] = p.m_StartColor;
                if ((p.m_Flags & ParticleSystem.Particle.FlagAgeInitialized) == 0) p.InitializeAge();
                alive[i] = p.m_AgePercent;
                inverse[i] = p.m_InvStartLifetime;
                seeds[i] = p.m_RandomSeed;
                if (meshIndices) meshes[i] = p.m_MeshIndex;
            }
            if (custom1) FillCustom(ps, 0, ref m_Custom1, count);
            if (custom2) FillCustom(ps, 1, ref m_Custom2, count);
        }

        static void FillCustom(ParticleSystem ps, int stream, ref NativeParticleData.Array4 a, int count)
        {
            var particles = ps.m_Particles;
            for (int i = 0; i < count; i++)
            {
                var v = ps.VertexCustomData(ref particles[i], i, stream);
                a.x[i] = v.x;
                a.y[i] = v.y;
                a.z[i] = v.z;
                a.w[i] = v.w;
            }
        }

        /// <summary>The arrays back into the particles (main thread, after the jobs).</summary>
        internal void Flush(ParticleSystem ps)
        {
            int count = Math.Min(m_Data->count, ps.m_Count);
            var particles = ps.m_Particles;
            var colors = (Color32*)m_Data->startColors;
            var alive = (float*)m_Data->aliveTimePercent;
            var inverse = (float*)m_Data->inverseStartLifetimes;
            var seeds = (uint*)m_Data->randomSeeds;
            var meshes = (int*)m_Data->meshIndices;
            for (int i = 0; i < count; i++)
            {
                ref var p = ref particles[i];
                p.m_Position = Get(ref m_Data->positions, i);
                p.m_Velocity = Get(ref m_Data->velocities, i);
                p.m_AxisOfRotation = Get(ref m_Data->axisOfRotations, i);
                p.m_Rotation = Get(ref m_Data->rotations, i);
                p.m_AngularVelocity = Get(ref m_Data->rotationalSpeeds, i);
                p.m_StartSize = Get(ref m_Data->sizes, i);
                p.m_StartColor = colors[i];
                if (alive[i] != p.m_AgePercent || inverse[i] != p.m_InvStartLifetime)
                {
                    p.m_AgePercent = alive[i];
                    p.m_InvStartLifetime = inverse[i];
                    p.ExportLifetime();
                }
                p.m_RandomSeed = seeds[i];
                if (meshes != null) p.m_MeshIndex = meshes[i];
            }
            if (m_Data->customData1.x != null)
            {
                FlushCustom(ps, 0, ref m_Custom1, count);
                m_CustomHeld0 = true;
                m_CustomRun0 = ps.m_CustomRun;
            }
            if (m_Data->customData2.x != null)
            {
                FlushCustom(ps, 1, ref m_Custom2, count);
                m_CustomHeld1 = true;
                m_CustomRun1 = ps.m_CustomRun;
            }
        }

        static void FlushCustom(ParticleSystem ps, int stream, ref NativeParticleData.Array4 a, int count)
        {
            var data = ps.EnsureCustom(stream);
            for (int i = 0; i < count; i++) data[i] = new Vector4(a.x[i], a.y[i], a.z[i], a.w[i]);
        }
    }
}
