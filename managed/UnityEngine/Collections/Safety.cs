using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>
    /// The native side of <see cref="AtomicSafetyHandle"/>, as the Editor runs it with
    /// ENABLE_UNITY_COLLECTIONS_CHECKS: version nodes, the scheduled jobs that read or write each
    /// container, temp memory scopes, and the errors Unity reports.
    ///
    /// A node is 16 aligned bytes of native memory: [0] the primary version, [1] the secondary
    /// version, [2] the node's index, [3] its kind. A version word is a multiple of
    /// <see cref="AtomicSafetyHandle.VersionIncrement"/> plus bits that deny the main thread
    /// reading (1), writing (2) and deallocating (4) while jobs use the container, and
    /// <see cref="AtomicSafetyHandle.TempVersion"/> on temp memory nodes. A handle matches its node
    /// when the versions are equal; the inline checks compare with the bits of the operation
    /// masked out, so an operation that is allowed never leaves managed code.
    ///
    /// A scheduled job's copy of a container's handle points at a shadow node of its own whose
    /// bits deny what the job may not do ([ReadOnly] writing, [WriteOnly] reading, deallocating),
    /// so checks inside jobs take the inline path too, without locks.
    /// </summary>
    internal static unsafe class Safety
    {
        internal const int Read = AtomicSafetyHandle.Read;
        internal const int Write = AtomicSafetyHandle.Write;
        internal const int Dispose = AtomicSafetyHandle.Dispose;
        const int AccessBits = Read | Write | Dispose;
        const int VersionMask = ~15;
        const int KindReal = 0;
        const int KindShadow = 0x5ad0;

        internal const string UnknownType = "UNKNOWN_OBJECT_TYPE";

        internal sealed class Node
        {
            public int* Memory;
            public int Index;
            public bool Temp;
            public bool Released = true;
            public bool AllowSecondaryWriting = true;
            public bool BumpSecondaryOnScheduleWrite;
            public bool AllowReadOrWrite = true;
            public bool Nested;
            public bool ExclusiveWeak;
            public bool Undisposable;
            /// <summary>The unsynced job that writes the container, and the field it uses.</summary>
            public Jobs.JobRecord Writer;
            public string WriterField;
            /// <summary>The unsynced jobs that read the container.</summary>
            public readonly List<(Jobs.JobRecord Job, string Field)> Readers = new List<(Jobs.JobRecord, string)>();
        }

        /// <summary>A job's view of a container: the node its copy of the handle points at.</summary>
        internal sealed class Shadow
        {
            public int* Memory;
            public int Index;
            public Node Real;
            public Jobs.JobRecord Job;
            /// <summary><see cref="Jobs.ContainerAccess"/> of the container in the job.</summary>
            public int Access;
            public int SecondaryAtSchedule;
        }

        static readonly object s_Lock = new object();
        static Node[] s_Nodes = new Node[256];
        static int s_NodeCount;
        static readonly Stack<Node> s_FreeNodes = new Stack<Node>();
        static Shadow[] s_Shadows = new Shadow[256];
        static int s_ShadowCount;
        static readonly Stack<Shadow> s_FreeShadows = new Stack<Shadow>();
        static int s_NextVersion = AtomicSafetyHandle.VersionIncrement;

        static int* AllocateNodeMemory()
        {
            var memory = (int*)NativeMemory.AlignedAlloc(16, 16);
            memory[0] = memory[1] = memory[2] = memory[3] = 0;
            return memory;
        }

        /// <summary>The node a handle's version pointer is in (the secondary version is its second int).</summary>
        static int* NodeBase(IntPtr versionNode) => (int*)((long)versionNode & ~15L);

        // ------------------------------------------------------------------ nodes

        /// <summary>A new node with fresh versions: no handle of an earlier use of its memory matches it.</summary>
        internal static AtomicSafetyHandle Create(bool temp)
        {
            Node node;
            lock (s_Lock)
            {
                if (s_FreeNodes.Count > 0)
                {
                    node = s_FreeNodes.Pop();
                }
                else
                {
                    node = new Node { Memory = AllocateNodeMemory(), Index = s_NodeCount };
                    if (s_NodeCount == s_Nodes.Length) Array.Resize(ref s_Nodes, s_Nodes.Length * 2);
                    s_Nodes[s_NodeCount++] = node;
                    node.Memory[2] = node.Index;
                    node.Memory[3] = KindReal;
                }
                // Versions only grow, so a handle to a released node never matches its next use.
                int version = s_NextVersion;
                s_NextVersion += AtomicSafetyHandle.VersionIncrement;
                if (s_NextVersion <= 0) s_NextVersion = AtomicSafetyHandle.VersionIncrement;
                node.Released = false;
                node.Temp = temp;
                node.AllowSecondaryWriting = true;
                node.BumpSecondaryOnScheduleWrite = false;
                node.AllowReadOrWrite = true;
                node.Nested = false;
                node.ExclusiveWeak = false;
                node.Undisposable = false;
                node.Writer = null;
                node.WriterField = null;
                node.Readers.Clear();
                int tempBit = temp ? AtomicSafetyHandle.TempVersion : 0;
                Volatile.Write(ref node.Memory[0], version | tempBit);
                Volatile.Write(ref node.Memory[1], version | tempBit);
            }
            return new AtomicSafetyHandle
            {
                versionNode = (IntPtr)node.Memory,
                version = node.Memory[0],
            };
        }

        /// <summary>The live node a handle belongs to, or null (default handle, released, shadow).</summary>
        static Node RealNode(in AtomicSafetyHandle handle)
        {
            if (handle.versionNode == IntPtr.Zero) return null;
            var node = NodeBase(handle.versionNode);
            if (node[3] != KindReal) return null;
            var n = s_Nodes[node[2]];
            return n.Released ? null : n;
        }

        static Shadow ShadowOf(in AtomicSafetyHandle handle)
        {
            if (handle.versionNode == IntPtr.Zero) return null;
            var node = NodeBase(handle.versionNode);
            return node[3] == KindShadow ? s_Shadows[node[2]] : null;
        }

        static bool IsSecondary(in AtomicSafetyHandle handle) => (long)handle.versionNode != (long)NodeBase(handle.versionNode);

        static bool Matches(in AtomicSafetyHandle handle) =>
            handle.versionNode != IntPtr.Zero &&
            (handle.version & VersionMask) == (Volatile.Read(ref *(int*)handle.versionNode) & VersionMask);

        internal static bool IsTemp(in AtomicSafetyHandle handle)
        {
            var node = RealNode(handle);
            return node != null && node.Temp && Matches(handle);
        }

        internal static void Release(in AtomicSafetyHandle handle)
        {
            lock (s_Lock)
            {
                var node = RealNode(handle);
                // Releasing an already released handle is allowed (Unity ignores it).
                if (node == null || !Matches(handle)) return;
                ReleaseNode(node);
            }
        }

        static void ReleaseNode(Node node)
        {
            int next = s_NextVersion;
            s_NextVersion += AtomicSafetyHandle.VersionIncrement;
            int tempBit = node.Temp ? AtomicSafetyHandle.TempVersion : 0;
            Volatile.Write(ref node.Memory[0], next | tempBit);
            Volatile.Write(ref node.Memory[1], next | tempBit);
            node.Released = true;
            node.Writer = null;
            node.Readers.Clear();
            s_FreeNodes.Push(node);
        }

        internal static void SetFlag(in AtomicSafetyHandle handle, Action<Node> set)
        {
            lock (s_Lock)
            {
                var node = RealNode(handle);
                if (node == null) return;
                set(node);
                UpdateBits(node);
            }
        }

        internal static bool GetFlag(in AtomicSafetyHandle handle, Func<Node, bool> get, bool fallback)
        {
            lock (s_Lock)
            {
                var node = RealNode(handle);
                return node != null ? get(node) : fallback;
            }
        }

        /// <summary>Sets the deny bits of a node's versions from the jobs using it and its flags.</summary>
        static void UpdateBits(Node node)
        {
            int deny = 0;
            if (node.Writer != null) deny |= Read | Write | Dispose;
            if (node.Readers.Count > 0) deny |= Write | Dispose;
            if (!node.AllowReadOrWrite) deny |= Read | Write;
            if (node.Undisposable) deny |= Dispose;
            int secondaryDeny = deny | (node.AllowSecondaryWriting ? 0 : Write);
            Volatile.Write(ref node.Memory[0], (node.Memory[0] & ~AccessBits) | deny);
            Volatile.Write(ref node.Memory[1], (node.Memory[1] & ~AccessBits) | secondaryDeny);
        }

        internal static void BumpSecondary(in AtomicSafetyHandle handle)
        {
            var node = NodeBase(handle.versionNode);
            Interlocked.Add(ref node[1], AtomicSafetyHandle.VersionIncrement);
        }

        // ------------------------------------------------------------------ temp memory scopes

        /// <summary>The temp memory handles of a scope (the main thread's frame, a job's execution),
        /// all released when it ends.</summary>
        internal sealed class TempScope
        {
            readonly List<AtomicSafetyHandle> m_Handles = new List<AtomicSafetyHandle>();
            readonly HashSet<IntPtr> m_Allocations = new HashSet<IntPtr>();

            public AtomicSafetyHandle NewHandle()
            {
                var handle = Create(temp: true);
                lock (m_Handles) m_Handles.Add(handle);
                return handle;
            }

            public void AddAllocation(IntPtr memory)
            {
                lock (m_Allocations) m_Allocations.Add(memory);
            }

            public void RemoveAllocation(IntPtr memory)
            {
                lock (m_Allocations) m_Allocations.Remove(memory);
            }

            /// <summary>Invalidates the scope's temp handles and frees its temp memory.</summary>
            public void End()
            {
                lock (m_Handles)
                {
                    if (m_Handles.Count > 0)
                    {
                        lock (s_Lock)
                        {
                            foreach (var handle in m_Handles)
                            {
                                var node = RealNode(handle);
                                if (node != null && Matches(handle)) ReleaseNode(node);
                            }
                        }
                        m_Handles.Clear();
                    }
                }
                List<IntPtr> allocations;
                lock (m_Allocations)
                {
                    if (m_Allocations.Count == 0) return;
                    allocations = new List<IntPtr>(m_Allocations);
                    m_Allocations.Clear();
                }
                Allocation.FreeScope(allocations);
            }
        }

        [ThreadStatic] static TempScope t_ThreadScope;
        static readonly TempScope s_MainScope = new TempScope();

        /// <summary>The scope temp memory allocated now belongs to: the executing job's, else the
        /// main thread's frame, else the thread's own.</summary>
        internal static TempScope CurrentScope
        {
            get
            {
                var job = Jobs.JobSystem.CurrentTempScope;
                if (job != null) return job;
                if (PlayerLoopDriver.IsMainThread) return s_MainScope;
                return t_ThreadScope ??= new TempScope();
            }
        }

        /// <summary>The end of the frame: Allocator.Temp memory of the main thread is gone.</summary>
        internal static void EndFrame() => s_MainScope.End();

        // ------------------------------------------------------------------ jobs

        /// <summary>A shadow node for a job's copy of <paramref name="handle"/>, denying what the job may
        /// not do; the copy is pointed at it.</summary>
        internal static void PatchForJob(ref AtomicSafetyHandle handle, Jobs.JobRecord job, int access, int deny)
        {
            var real = RealNode(handle);
            Shadow shadow;
            lock (s_Lock)
            {
                if (s_FreeShadows.Count > 0)
                {
                    shadow = s_FreeShadows.Pop();
                }
                else
                {
                    shadow = new Shadow { Memory = AllocateNodeMemory(), Index = s_ShadowCount };
                    if (s_ShadowCount == s_Shadows.Length) Array.Resize(ref s_Shadows, s_Shadows.Length * 2);
                    s_Shadows[s_ShadowCount++] = shadow;
                    shadow.Memory[2] = shadow.Index;
                    shadow.Memory[3] = KindShadow;
                }
                shadow.Real = real;
                shadow.Job = job;
                shadow.Access = access;
                var node = NodeBase(handle.versionNode);
                int primary = node[0] & VersionMask, secondary = node[1] & VersionMask;
                int secondaryDeny = deny | (real != null && !real.AllowSecondaryWriting ? Write : 0);
                shadow.Memory[0] = primary | deny;
                shadow.Memory[1] = secondary | secondaryDeny;
                shadow.SecondaryAtSchedule = secondary;
            }
            bool secondaryHandle = IsSecondary(handle);
            handle.versionNode = (IntPtr)(shadow.Memory + (secondaryHandle ? 1 : 0));
            handle.version &= VersionMask;
            job.Shadows.Add(shadow);
        }

        /// <summary>Checks a container a job is about to use against the unsynced jobs using it;
        /// returns the error Unity reports, or null.</summary>
        internal static string CheckSchedule(in AtomicSafetyHandle handle, Jobs.JobRecord job, bool writes, string jobName, string containerType, string field, Func<Jobs.JobRecord, bool> isDependency)
        {
            lock (s_Lock)
            {
                var node = RealNode(handle);
                if (node == null) return null;
                if (node.Writer != null && node.Writer != job && !isDependency(node.Writer))
                    return Conflict(node.Writer.Name, "writes to", node.WriterField, jobName, writes, containerType, field);
                if (writes)
                {
                    foreach (var (reader, readerField) in node.Readers)
                        if (reader != job && !isDependency(reader))
                            return Conflict(reader.Name, "reads from", readerField, jobName, true, containerType, field);
                }
                return null;
            }
        }

        static string Conflict(string previous, string verb, string previousField, string jobName, bool writes, string type, string field) =>
            $"The previously scheduled job {previous} {verb} the {type} {previousField}. You are trying to schedule a new job {jobName}, which {(writes ? "writes to" : "reads from")} the same {type} (via {field}). To guarantee safety, you must include {previous} as a dependency of the newly scheduled job.";

        /// <summary>Registers a scheduled job as a reader or the writer of a container.</summary>
        internal static void AddJobUse(in AtomicSafetyHandle handle, Jobs.JobRecord job, bool writes, string field)
        {
            lock (s_Lock)
            {
                var node = RealNode(handle);
                if (node == null) return;
                if (writes)
                {
                    // The previous users are dependencies of this job: syncing it syncs them.
                    node.Writer = job;
                    node.WriterField = field;
                    node.Readers.Clear();
                    if (node.BumpSecondaryOnScheduleWrite)
                        Interlocked.Add(ref node.Memory[1], AtomicSafetyHandle.VersionIncrement);
                }
                else
                {
                    bool known = false;
                    foreach (var (reader, _) in node.Readers) known |= reader == job;
                    if (!known) node.Readers.Add((job, field));
                }
                UpdateBits(node);
                job.Nodes.Add(node);
            }
        }

        /// <summary>After a job synced: it no longer holds its containers, and the secondary
        /// versions it bumped (a list resized in the job) reach the containers.</summary>
        internal static void JobSynced(Jobs.JobRecord job)
        {
            lock (s_Lock)
            {
                foreach (var node in job.Nodes)
                {
                    if (node.Writer == job)
                    {
                        node.Writer = null;
                        node.WriterField = null;
                    }
                    node.Readers.RemoveAll(r => r.Job == job);
                    if (!node.Released) UpdateBits(node);
                }
                job.Nodes.Clear();
                foreach (var shadow in job.Shadows)
                {
                    int bumped = (shadow.Memory[1] & VersionMask) - shadow.SecondaryAtSchedule;
                    if (bumped != 0 && shadow.Real != null && !shadow.Real.Released)
                        Interlocked.Add(ref shadow.Real.Memory[1], bumped);
                    // Stale copies of the job's data no longer match anything.
                    shadow.Memory[0] = shadow.Memory[1] = 0;
                    shadow.Real = null;
                    shadow.Job = null;
                    s_FreeShadows.Push(shadow);
                }
                job.Shadows.Clear();
            }
        }

        /// <summary>The unsynced jobs that use a container.</summary>
        internal static List<Jobs.JobRecord> JobsUsing(in AtomicSafetyHandle handle)
        {
            var jobs = new List<Jobs.JobRecord>();
            lock (s_Lock)
            {
                var node = RealNode(handle);
                if (node == null) return jobs;
                if (node.Writer != null) jobs.Add(node.Writer);
                foreach (var (reader, _) in node.Readers) jobs.Add(reader);
            }
            return jobs;
        }

        internal static Jobs.JobRecord WriterOf(in AtomicSafetyHandle handle)
        {
            lock (s_Lock) return RealNode(handle)?.Writer;
        }

        internal static List<Jobs.JobRecord> ReadersOf(in AtomicSafetyHandle handle)
        {
            var jobs = new List<Jobs.JobRecord>();
            lock (s_Lock)
            {
                var node = RealNode(handle);
                if (node != null)
                    foreach (var (reader, _) in node.Readers) jobs.Add(reader);
            }
            return jobs;
        }

        internal static bool IsJobCopy(in AtomicSafetyHandle handle) => ShadowOf(handle) != null;

        // ------------------------------------------------------------------ static safety ids and errors

        static readonly List<string> s_TypeNames = new List<string> { null };
        static readonly Dictionary<string, int> s_TypeIds = new Dictionary<string, int>();
        static readonly Dictionary<(int, AtomicSafetyErrorType), string> s_CustomMessages = new Dictionary<(int, AtomicSafetyErrorType), string>();

        internal static int NewStaticSafetyId(string ownerTypeName)
        {
            lock (s_TypeNames)
            {
                // The same owner type gets the same id, as in Unity.
                if (s_TypeIds.TryGetValue(ownerTypeName, out int id)) return id;
                s_TypeNames.Add(ownerTypeName);
                id = s_TypeNames.Count - 1;
                s_TypeIds[ownerTypeName] = id;
                return id;
            }
        }

        internal static void SetCustomErrorMessage(int staticSafetyId, AtomicSafetyErrorType errorType, string message)
        {
            lock (s_TypeNames)
            {
                if (staticSafetyId <= 0 || staticSafetyId >= s_TypeNames.Count)
                    throw new ArgumentException("Invalid static safety id " + staticSafetyId);
                s_CustomMessages[(staticSafetyId, errorType)] = message;
            }
        }

        internal static string TypeName(int staticSafetyId)
        {
            lock (s_TypeNames)
                return staticSafetyId > 0 && staticSafetyId < s_TypeNames.Count ? s_TypeNames[staticSafetyId] : UnknownType;
        }

        static string CustomMessage(int staticSafetyId, AtomicSafetyErrorType errorType)
        {
            lock (s_TypeNames)
                return s_CustomMessages.TryGetValue((staticSafetyId, errorType), out var message) ? message : null;
        }

        static Exception Deallocated(in AtomicSafetyHandle handle) =>
            new ObjectDisposedException(null, CustomMessage(handle.staticSafetyId, AtomicSafetyErrorType.Deallocated)
                ?? $"The {TypeName(handle.staticSafetyId)} has been deallocated, it is not allowed to access it");

        static string Operation(int operation) => operation switch
        {
            Read => "read from",
            Write => "write to",
            _ => "deallocate",
        };

        /// <summary>The checks the inline paths of CheckRead/Write/DeallocateAndThrow fall back to:
        /// throws what Unity throws, or returns when the operation is allowed after all.</summary>
        internal static void CheckAndThrow(in AtomicSafetyHandle handle, int operation)
        {
            // A default handle has no node: reading it fails as in the Editor (a null reference).
            int word = Volatile.Read(ref *(int*)handle.versionNode);
            var type = TypeName(handle.staticSafetyId);
            var shadow = ShadowOf(handle);
            if (shadow != null)
            {
                if ((handle.version & VersionMask) != (word & VersionMask)) throw Deallocated(handle);
                if ((word & operation) == 0) return;
                if (operation == Dispose)
                    throw new InvalidOperationException(CustomMessage(handle.staticSafetyId, AtomicSafetyErrorType.DeallocatedFromJob)
                        ?? "The native container may not be deallocated on a job.");
                if (operation == Write && (shadow.Access & Jobs.ContainerAccess.Write) != 0 && IsSecondary(handle))
                    throw new InvalidOperationException($"The {type} has been declared as [ReadOnly], but you are writing to it.");
                if (operation == Write)
                    throw new InvalidOperationException($"The {type} has been declared as [ReadOnly] in the job, but you are writing to it.");
                throw new InvalidOperationException($"The {type} has been declared as [WriteOnly] in the job, but you are reading from it.");
            }
            Node node;
            Jobs.JobRecord writer;
            string writerField;
            (Jobs.JobRecord Job, string Field) reader = default;
            lock (s_Lock)
            {
                node = RealNode(handle);
                if (node == null || (handle.version & VersionMask) != (word & VersionMask)) throw Deallocated(handle);
                writer = node.Writer;
                writerField = node.WriterField;
                if (node.Readers.Count > 0) reader = node.Readers[0];
            }
            if (operation != Dispose && !node.AllowReadOrWrite)
                throw new InvalidOperationException($"The {type} can no longer be accessed, since its owner has been invalidated. You can simply Dispose() the container and create a new one.");
            if (operation == Write && IsSecondary(handle) && !node.AllowSecondaryWriting)
                throw new InvalidOperationException($"The {type} has been declared as [ReadOnly], but you are writing to it.");
            if (operation == Dispose && node.Undisposable)
                throw new InvalidOperationException($"The {type} has been set to undisposable and cannot be deallocated. ");
            if (writer != null)
                throw new InvalidOperationException($"The previously scheduled job {writer.Name} writes to the {type} {writerField}. You must call JobHandle.Complete() on the job {writer.Name}, before you can {Operation(operation)} the {type} safely.");
            if (operation != Read && reader.Job != null)
                throw new InvalidOperationException($"The previously scheduled job {reader.Job.Name} reads from the {type} {reader.Field}. You must call JobHandle.Complete() on the job {reader.Job.Name}, before you can {Operation(operation)} the {type} safely.");
        }

        /// <summary>Whether the handle is the main thread's valid view of its node (the checks the
        /// job scheduler makes on a container).</summary>
        internal static bool IsAlive(in AtomicSafetyHandle handle)
        {
            var node = RealNode(handle);
            return node != null && Matches(handle);
        }

        internal static bool IsNested(in AtomicSafetyHandle handle) => RealNode(handle)?.Nested ?? false;
    }
}
