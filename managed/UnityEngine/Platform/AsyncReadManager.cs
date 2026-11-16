using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unbound.Runtime;
using Unbound.Runtime.Jobs;

namespace Unity.IO.LowLevel.Unsafe
{
    public enum ReadStatus
    {
        Complete = 0,
        InProgress = 1,
        Failed = 2,
        Truncated = 4,
        Canceled = 5,
    }

    public enum FileState
    {
        Absent = 0,
        Exists = 1,
    }

    public enum FileStatus
    {
        Closed = 0,
        Pending = 1,
        Open = 2,
        OpenFailed = 3,
    }

    public enum AssetLoadingSubsystem
    {
        Other = 0,
        Texture = 1,
        VirtualTexture = 2,
        Mesh = 3,
        Audio = 4,
        Scripts = 5,
        EntitiesScene = 6,
        EntitiesStreamBinaryReader = 7,
        FileInfo = 8,
        ContentLoading = 9,
    }

    public unsafe partial struct ReadCommand
    {
        public void* Buffer;
        public long Offset;
        public long Size;
    }

    public unsafe partial struct ReadCommandArray
    {
        public ReadCommand* ReadCommands;
        public int CommandCount;
    }

    public partial struct FileInfoResult
    {
        public long FileSize;
        public FileState FileState;
    }

    /// <summary>A handle to an asynchronous read request; the checks and messages are the
    /// release's.</summary>
    public unsafe partial struct ReadHandle : IDisposable
    {
        [NativeDisableUnsafePtrRestriction]
        internal IntPtr ptr;
        internal int version;

        public JobHandle JobHandle
        {
            get
            {
                if (!IsValid()) throw new InvalidOperationException("ReadHandle.JobHandle cannot be called after the ReadHandle has been disposed");
                return AsyncFileIO.Request(this).Job;
            }
        }

        public ReadStatus Status
        {
            get
            {
                if (!IsValid()) throw new InvalidOperationException("Cannot use a ReadHandle that has been disposed");
                return AsyncFileIO.Request(this).Status;
            }
        }

        public long ReadCount
        {
            get
            {
                if (!IsValid()) throw new InvalidOperationException("Cannot use a ReadHandle that has been disposed");
                return AsyncFileIO.Request(this).ReadCount;
            }
        }

        public bool IsValid() => AsyncFileIO.Request(this) != null;

        public void Dispose()
        {
            if (!IsValid()) throw new InvalidOperationException("ReadHandle.Dispose cannot be called twice on the same ReadHandle");
            if (Status == ReadStatus.InProgress) throw new InvalidOperationException("ReadHandle.Dispose cannot be called until the read operation completes");
            AsyncFileIO.Release(this);
        }

        public void Cancel()
        {
            if (!IsValid()) throw new InvalidOperationException("ReadHandle.Cancel cannot be called on a disposed ReadHandle");
            AsyncFileIO.Request(this)?.Cancel();
        }

        public long GetBytesRead()
        {
            if (!IsValid()) throw new InvalidOperationException("ReadHandle.GetBytesRead cannot be called after the ReadHandle has been disposed");
            return AsyncFileIO.Request(this).TotalBytesRead();
        }

        public long GetBytesRead(uint readCommandIndex)
        {
            if (!IsValid()) throw new InvalidOperationException("ReadHandle.GetBytesRead cannot be called after the ReadHandle has been disposed");
            return AsyncFileIO.Request(this).BytesReadOf(readCommandIndex);
        }

        public ulong* GetBytesReadArray()
        {
            if (!IsValid()) throw new InvalidOperationException("ReadHandle.GetBytesReadArray cannot be called after the ReadHandle has been disposed");
            return AsyncFileIO.Request(this).BytesRead;
        }
    }

    /// <summary>A handle to a file opened by <see cref="AsyncReadManager.OpenFileAsync"/>.</summary>
    public readonly partial struct FileHandle
    {
        [NativeDisableUnsafePtrRestriction]
        internal readonly IntPtr fileCommandPtr;
        internal readonly int version;

        internal FileHandle(IntPtr fileCommandPtr, int version)
        {
            this.fileCommandPtr = fileCommandPtr;
            this.version = version;
        }

        public FileStatus Status
        {
            get
            {
                if (!IsValid()) throw new InvalidOperationException("FileHandle.Status cannot be called on a closed FileHandle");
                return AsyncFileIO.File(this).Status;
            }
        }

        public JobHandle JobHandle
        {
            get
            {
                if (!IsValid()) throw new InvalidOperationException("FileHandle.JobHandle cannot be called on a closed FileHandle");
                return AsyncFileIO.File(this).OpenJob;
            }
        }

        public bool IsValid() => AsyncFileIO.File(this) != null;

        public JobHandle Close(JobHandle dependency = default)
        {
            if (!IsValid()) throw new InvalidOperationException("FileHandle.Close cannot be called twice on the same FileHandle");
            return AsyncReadManager.CloseFileAsync(this, dependency);
        }
    }

    /// <summary>Asynchronous file reads. Each request runs as one job of the job system, so its
    /// JobHandle completes, chains and reports completion as other jobs do; paths resolve against
    /// the working directory (the project in the Editor). Outcomes match the Unity 6000.0.71f1
    /// Editor, tests/projects/asyncread.</summary>
    public static unsafe partial class AsyncReadManager
    {
        public static ReadHandle Read(string filename, ReadCommand* readCmds, uint readCmdCount, string assetName = "", ulong typeID = 0UL, AssetLoadingSubsystem subsystem = AssetLoadingSubsystem.Scripts) =>
            AsyncFileIO.ReadByName(filename, readCmds, readCmdCount, assetName, typeID, subsystem);

        public static ReadHandle GetFileInfo(string filename, FileInfoResult* result)
        {
            if (result == null) throw new NullReferenceException("GetFileInfo must have a valid FileInfoResult to write into.");
            return AsyncFileIO.GetFileInfo(filename, result);
        }

        public static ReadHandle ReadDeferred(in FileHandle fileHandle, ReadCommandArray* readCmdArray, JobHandle dependency)
        {
            if (!fileHandle.IsValid()) throw new InvalidOperationException("FileHandle is invalid and may not be read from.");
            return AsyncFileIO.ReadFile(AsyncFileIO.File(fileHandle), default, readCmdArray, dependency);
        }

        public static ReadHandle Read(in FileHandle fileHandle, ReadCommandArray readCmdArray)
        {
            if (!fileHandle.IsValid()) throw new InvalidOperationException("FileHandle is invalid and may not be read from.");
            return AsyncFileIO.ReadFile(AsyncFileIO.File(fileHandle), readCmdArray, null, default);
        }

        public static FileHandle OpenFileAsync(string fileName)
        {
            // The release reads fileName.Length first: a null name is Mono's NullReferenceException.
            if (fileName == null) throw new NullReferenceException("Object reference not set to an instance of an object");
            if (fileName.Length == 0) throw new InvalidOperationException("FileName is empty");
            return AsyncFileIO.Open(fileName);
        }

        [UnityEngine.ThreadAndSerializationSafe]
        internal static JobHandle CloseFileAsync(in FileHandle fileHandle, JobHandle dependency) => AsyncFileIO.Close(fileHandle, dependency);

        /// <summary>Unbound keeps no cache of files read by name (each read opens and closes its
        /// file), so there is nothing to close: the handle completes with the dependency.</summary>
        [UnityEngine.ThreadAndSerializationSafe]
        public static JobHandle CloseCachedFileAsync(string fileName, JobHandle dependency = default) => dependency;
    }
}

namespace Unbound.Runtime
{
    using Unity.IO.LowLevel.Unsafe;

    /// <summary>
    /// The requests behind <see cref="ReadHandle"/> and <see cref="FileHandle"/>: slots with a
    /// version, so a disposed or closed handle (and a default one) is invalid and a reused slot
    /// does not answer for an older handle. Every request is its own object, and its job holds
    /// that object, so a job that runs late acts only on the request it was made for.
    /// </summary>
    internal static unsafe class AsyncFileIO
    {
        static readonly object s_Lock = new object();
        static readonly List<ReadRequest> s_Reads = new List<ReadRequest>();
        static readonly Stack<int> s_FreeReads = new Stack<int>();
        static readonly List<OpenFile> s_Files = new List<OpenFile>();
        static readonly Stack<int> s_FreeFiles = new Stack<int>();
        static readonly List<int> s_ReadVersions = new List<int>();
        static readonly List<int> s_FileVersions = new List<int>();

        /// <summary>Failures of opening or reading a file: the request reports them in its status.</summary>
        static bool IsFileError(Exception e) =>
            e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException || e is ObjectDisposedException;

        internal sealed class OpenFile
        {
            internal readonly string Path;
            internal SafeFileHandle Handle;
            /// <summary>The path names a directory: it opens, as on POSIX, and its reads fail
            /// (asyncread open.directory.*, measured on Linux).</summary>
            internal bool Directory;
            internal volatile FileStatus Status = FileStatus.Pending;
            internal JobHandle OpenJob;
            /// <summary>The reads queued on the file that may not have run: a close waits for
            /// them. Each with the job of the caller's it waits for, if any.</summary>
            internal readonly List<(JobHandle Job, JobHandle Caller)> Reads = new List<(JobHandle, JobHandle)>();

            internal OpenFile(string path) => Path = path;

            internal void RunOpen()
            {
                try
                {
                    if (System.IO.Directory.Exists(Path))
                    {
                        Directory = true;
                        Status = FileStatus.Open;
                        return;
                    }
                    Handle = System.IO.File.OpenHandle(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    Status = FileStatus.Open;
                }
                catch (Exception e) when (IsFileError(e))
                {
                    Status = FileStatus.OpenFailed;
                }
            }

            internal void RunClose()
            {
                Handle?.Dispose();
                Handle = null;
                Status = FileStatus.Closed;
            }
        }

        internal enum Kind
        {
            ReadByName,
            ReadFile,
            FileInfo,
        }

        internal sealed class ReadRequest
        {
            internal Kind Kind;
            internal string Path;
            internal OpenFile File;
            internal FileInfoResult* InfoResult;
            /// <summary>The request's own copy of its commands (Read copies them).</summary>
            internal ReadCommand* Commands;
            internal int CommandCount;
            /// <summary>ReadDeferred: the caller's array, read when the request starts.</summary>
            internal ReadCommandArray* Deferred;
            internal ulong* BytesRead;
            internal long ReadCount;
            int m_Status = (int)ReadStatus.InProgress;
            internal JobHandle Job;
            /// <summary>The request's records when it was made while AsyncReadManagerMetrics
            /// collected; null otherwise.</summary>
            internal AsyncReadMetrics.Recording Metrics;

            internal ReadStatus Status => (ReadStatus)Volatile.Read(ref m_Status);

            /// <summary>Settles the request unless it already settled (a cancel wins over a read
            /// that had not finished).</summary>
            bool Settle(ReadStatus status) =>
                Interlocked.CompareExchange(ref m_Status, (int)status, (int)ReadStatus.InProgress) == (int)ReadStatus.InProgress;

            /// <summary>A pending request turns Canceled at once and does not read; a settled one
            /// keeps its outcome (asyncread: cancel.*, *.cancelAfterCompletion).</summary>
            internal void Cancel()
            {
                if (Settle(ReadStatus.Canceled)) Metrics?.Cancel();
            }

            /// <summary>The bytes read so far: each command's count is written when the command
            /// finishes, so a read in progress reports its progress.</summary>
            internal long TotalBytesRead()
            {
                long total = 0;
                int count = Volatile.Read(ref CommandCount);
                ulong* bytes = BytesRead;
                if (bytes != null)
                    for (int i = 0; i < count; i++) total += (long)Volatile.Read(ref bytes[i]);
                return total;
            }

            internal long BytesReadOf(uint index)
            {
                ulong* bytes = BytesRead;
                return bytes != null && index < (uint)Volatile.Read(ref CommandCount) ? (long)Volatile.Read(ref bytes[index]) : 0;
            }

            /// <summary>Runs on a job worker; holds the request so that releasing its memory
            /// waits for a read in progress (a canceled read may be disposed while it runs).</summary>
            internal void Run()
            {
                lock (this)
                {
                    if (Status == ReadStatus.InProgress)
                    {
                        if (Kind == Kind.FileInfo) RunFileInfo();
                        else RunReads();
                    }
                    // A cancel that came while the request ran, or before its deferred commands
                    // were taken, leaves the records it did not reach canceled.
                    if (Status == ReadStatus.Canceled) Metrics?.Cancel();
                }
            }

            void RunReads()
            {
                if (Deferred != null)
                {
                    TakeCommands(Deferred->ReadCommands, Deferred->CommandCount);
                    Metrics?.Commands(Commands, CommandCount);
                }
                SafeFileHandle owned = Kind == Kind.ReadByName ? OpenForRead(Path) : null;
                SafeFileHandle handle = Kind == Kind.ReadByName ? owned : File.Handle;
                bool readable = handle != null && (Kind == Kind.ReadByName || (File.Status == FileStatus.Open && !File.Directory));
                // A directory opens (its reads fail); a missing file does not, and its commands
                // spent no time queued (asyncread metrics.directory.all, metrics.all).
                bool opened = Kind == Kind.ReadByName ? owned != null || (!string.IsNullOrEmpty(Path) && Directory.Exists(Path)) : File.Status == FileStatus.Open;
                try
                {
                    var outcome = ReadStatus.Complete;
                    for (int i = 0; i < CommandCount; i++)
                    {
                        if (Status != ReadStatus.InProgress) return;
                        var command = Commands[i];
                        if (opened) Metrics?.Start(i);
                        long read = readable ? ReadOne(handle, command) : 0;
                        Volatile.Write(ref BytesRead[i], (ulong)read);
                        Metrics?.Finish(i, command.Size, read, opened);
                        Interlocked.Increment(ref ReadCount);
                        // Unity 6000.0.71f1 (asyncread name.*, open.*): a command that reads
                        // nothing fails the request, one cut short by the end of the file
                        // truncates it; every command runs either way.
                        if (command.Size > 0 && read == 0) outcome = ReadStatus.Failed;
                        else if (read < command.Size && outcome != ReadStatus.Failed) outcome = ReadStatus.Truncated;
                    }
                    Settle(outcome);
                }
                finally
                {
                    owned?.Dispose();
                }
            }

            void RunFileInfo()
            {
                long size = 0;
                var state = FileState.Absent;
                try
                {
                    // A directory, a missing file and no name are all Absent (asyncread info.*).
                    if (!string.IsNullOrEmpty(Path) && System.IO.File.Exists(Path))
                    {
                        size = new FileInfo(Path).Length;
                        state = FileState.Exists;
                    }
                }
                catch (Exception e) when (IsFileError(e))
                {
                    size = 0;
                    state = FileState.Absent;
                }
                InfoResult->FileSize = size;
                InfoResult->FileState = state;
                Metrics?.Finish(0, 0, 0, true);
                Settle(ReadStatus.Complete);
            }

            internal void TakeCommands(ReadCommand* commands, int count)
            {
                count = commands == null ? 0 : Math.Max(count, 0);
                if (count == 0) return;
                long bytes = (long)sizeof(ReadCommand) * count;
                Commands = (ReadCommand*)NativeMemory.Alloc((nuint)bytes);
                Buffer.MemoryCopy(commands, Commands, bytes, bytes);
                BytesRead = (ulong*)NativeMemory.AllocZeroed((nuint)(sizeof(ulong) * count));
                // Published last: a reader that sees the count sees the counts' memory.
                Volatile.Write(ref CommandCount, count);
            }

            internal void FreeMemory()
            {
                lock (this)
                {
                    Volatile.Write(ref CommandCount, 0);
                    var commands = Commands;
                    var bytes = BytesRead;
                    Commands = null;
                    BytesRead = null;
                    if (commands != null) NativeMemory.Free(commands);
                    if (bytes != null) NativeMemory.Free(bytes);
                }
            }
        }

        static SafeFileHandle OpenForRead(string path)
        {
            if (string.IsNullOrEmpty(path) || Directory.Exists(path)) return null;
            try { return System.IO.File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }
            catch (Exception e) when (IsFileError(e)) { return null; }
        }

        /// <summary>Reads one command: as many bytes as the file has from its offset, up to its
        /// size.</summary>
        static long ReadOne(SafeFileHandle handle, ReadCommand command)
        {
            if (command.Size <= 0 || command.Offset < 0 || command.Buffer == null) return 0;
            long total = 0;
            try
            {
                while (total < command.Size)
                {
                    int chunk = (int)Math.Min(command.Size - total, int.MaxValue);
                    int read = RandomAccess.Read(handle, new Span<byte>((byte*)command.Buffer + total, chunk), command.Offset + total);
                    if (read <= 0) break;
                    total += read;
                }
            }
            catch (Exception e) when (IsFileError(e))
            {
            }
            return total;
        }

        enum Work
        {
            Read,
            Open,
            Close,
        }

        /// <summary>One request's work; it holds its request (or file) through a GCHandle that it
        /// frees when it runs.</summary>
        struct RequestJob : IJob
        {
            public IntPtr Target;
            public Work Work;

            public void Execute()
            {
                var handle = GCHandle.FromIntPtr(Target);
                var target = handle.Target;
                handle.Free();
                switch (Work)
                {
                    case Work.Read: ((ReadRequest)target).Run(); break;
                    case Work.Open: ((OpenFile)target).RunOpen(); break;
                    case Work.Close: ((OpenFile)target).RunClose(); break;
                }
            }
        }

        /// <summary>The requests' jobs: each is synced once it ran, as a native request's
        /// resources are released, so a caller that never completes it does not keep it. One
        /// that waits for jobs of the caller's is synced only after the caller synced those
        /// (syncing it syncs its dependencies, which must stay the caller's to complete).</summary>
        static readonly List<(JobHandle Job, JobHandle[] Callers)> s_Owned = new List<(JobHandle, JobHandle[])>();

        static void Own(JobHandle job, JobHandle[] callers)
        {
            lock (s_Lock) s_Owned.Add((job, callers));
        }

        static bool CallersSynced(JobHandle[] callers)
        {
            if (callers == null) return true;
            foreach (var caller in callers)
                if (!Synced(JobSystem.RecordOf(caller))) return false;
            return true;
        }

        /// <summary>A caller's job is synced once completed; a combination of its jobs once the
        /// jobs it combines are (a caller need not complete the combined handle itself).</summary>
        static bool Synced(Jobs.JobRecord record)
        {
            if (record == null || record.Synced) return true;
            if (record.Reflection != null || !record.Done) return false;
            foreach (var dependency in record.Dependencies)
                if (!Synced(dependency)) return false;
            return true;
        }

        /// <summary>Syncs the owned jobs that may be (on the main thread, where syncing belongs).</summary>
        static void Sweep()
        {
            if (!PlayerLoopDriver.IsMainThread) return;
            List<Jobs.JobRecord> done = null;
            lock (s_Lock)
            {
                for (int i = s_Owned.Count - 1; i >= 0; i--)
                {
                    var record = JobSystem.RecordOf(s_Owned[i].Job);
                    if (record != null && (!record.Done || !CallersSynced(s_Owned[i].Callers))) continue;
                    if (record != null) (done ??= new List<Jobs.JobRecord>()).Add(record);
                    s_Owned.RemoveAt(i);
                }
            }
            if (done != null)
                foreach (var record in done) JobSystem.Sync(record);
        }

        static JobHandle Schedule(object target, Work work, JobHandle dependency)
        {
            Sweep();
            var handle = new RequestJob { Target = GCHandle.ToIntPtr(GCHandle.Alloc(target)), Work = work }.Schedule(dependency);
            // Unity starts a request when it is made, not when the caller's jobs are flushed.
            JobSystem.Start(handle);
            return handle;
        }

        static int Add<T>(List<T> slots, Stack<int> free, List<int> versions, T item)
        {
            int slot;
            if (free.Count > 0)
            {
                slot = free.Pop();
                slots[slot] = item;
            }
            else
            {
                slot = slots.Count;
                slots.Add(item);
                versions.Add(0);
            }
            versions[slot]++;
            return slot;
        }

        static ReadHandle Start(ReadRequest request, JobHandle dependency)
        {
            int slot, version;
            lock (s_Lock)
            {
                slot = Add(s_Reads, s_FreeReads, s_ReadVersions, request);
                version = s_ReadVersions[slot];
            }
            request.Job = Schedule(request, Work.Read, dependency);
            return new ReadHandle { ptr = (IntPtr)(slot + 1), version = version };
        }

        internal static ReadRequest Request(ReadHandle handle)
        {
            int slot = (int)handle.ptr - 1;
            lock (s_Lock)
                return slot >= 0 && slot < s_Reads.Count && s_ReadVersions[slot] == handle.version ? s_Reads[slot] : null;
        }

        internal static OpenFile File(in FileHandle handle)
        {
            int slot = (int)handle.fileCommandPtr - 1;
            lock (s_Lock)
                return slot >= 0 && slot < s_Files.Count && s_FileVersions[slot] == handle.version ? s_Files[slot] : null;
        }

        internal static ReadHandle ReadByName(string path, ReadCommand* commands, uint count, string assetName, ulong typeId, AssetLoadingSubsystem subsystem)
        {
            var request = new ReadRequest { Kind = Kind.ReadByName, Path = path };
            request.TakeCommands(commands, (int)Math.Min(count, int.MaxValue));
            request.Metrics = AsyncReadMetrics.Begin(path, assetName, typeId, subsystem);
            request.Metrics?.Commands(request.Commands, request.CommandCount);
            var handle = Start(request, default);
            Own(request.Job, null);
            return handle;
        }

        internal static ReadHandle GetFileInfo(string path, FileInfoResult* result)
        {
            var request = new ReadRequest { Kind = Kind.FileInfo, Path = path, InfoResult = result };
            request.Metrics = AsyncReadMetrics.Begin(path, "", 0, AssetLoadingSubsystem.FileInfo);
            request.Metrics?.FileInfo();
            var handle = Start(request, default);
            Own(request.Job, null);
            return handle;
        }

        internal static ReadHandle ReadFile(OpenFile file, ReadCommandArray commands, ReadCommandArray* deferred, JobHandle dependency)
        {
            var request = new ReadRequest { Kind = Kind.ReadFile, File = file, Deferred = deferred };
            if (deferred == null) request.TakeCommands(commands.ReadCommands, commands.CommandCount);
            request.Metrics = AsyncReadMetrics.Begin("", "", 0, AssetLoadingSubsystem.Scripts);
            if (deferred == null) request.Metrics?.Commands(request.Commands, request.CommandCount);
            else request.Metrics?.Commands(deferred->ReadCommands, deferred->CommandCount);
            // The read waits for the open (Unity: "The read will automatically wait for the
            // FileHandle.JobHandle to be complete").
            var handle = Start(request, JobHandle.CombineDependencies(dependency, file.OpenJob));
            bool callerDependency = !dependency.Equals(default(JobHandle));
            Own(request.Job, callerDependency ? new[] { dependency } : null);
            lock (file.Reads)
            {
                file.Reads.RemoveAll(read => JobSystem.RecordOf(read.Job) is not { Done: false });
                file.Reads.Add((request.Job, dependency));
            }
            return handle;
        }

        internal static void Release(ReadHandle handle)
        {
            ReadRequest request;
            lock (s_Lock)
            {
                request = Request(handle);
                if (request == null) return;
                int slot = (int)handle.ptr - 1;
                s_ReadVersions[slot]++;
                s_Reads[slot] = null;
                s_FreeReads.Push(slot);
            }
            Sweep();
            request.FreeMemory();
        }

        internal static FileHandle Open(string path)
        {
            var file = new OpenFile(path);
            int slot, version;
            lock (s_Lock)
            {
                slot = Add(s_Files, s_FreeFiles, s_FileVersions, file);
                version = s_FileVersions[slot];
            }
            file.OpenJob = Schedule(file, Work.Open, default);
            Own(file.OpenJob, null);
            return new FileHandle((IntPtr)(slot + 1), version);
        }

        /// <summary>Closes after the dependency, the open and the reads queued on the file (asyncread
        /// open.closeWithoutDependency); the handle is invalid at once (open.validAfterClose).</summary>
        internal static JobHandle Close(in FileHandle handle, JobHandle dependency)
        {
            OpenFile file;
            lock (s_Lock)
            {
                file = File(handle);
                if (file == null) return default;
                int slot = (int)handle.fileCommandPtr - 1;
                s_FileVersions[slot]++;
                s_Files[slot] = null;
                s_FreeFiles.Push(slot);
            }
            List<JobHandle> callers = null;
            if (!dependency.Equals(default(JobHandle))) (callers = new List<JobHandle>()).Add(dependency);
            JobHandle after;
            lock (file.Reads)
            {
                int capacity = file.Reads.Count + 2;
                Span<JobHandle> jobs = capacity <= 64 ? stackalloc JobHandle[capacity] : new JobHandle[capacity];
                int count = 0;
                jobs[count++] = dependency;
                jobs[count++] = file.OpenJob;
                foreach (var read in file.Reads)
                {
                    jobs[count++] = read.Job;
                    if (!read.Caller.Equals(default(JobHandle))) (callers ??= new List<JobHandle>()).Add(read.Caller);
                }
                file.Reads.Clear();
                fixed (JobHandle* pointer = jobs) after = JobSystem.Combine(pointer, count);
            }
            var close = Schedule(file, Work.Close, after);
            Own(close, callers?.ToArray());
            return close;
        }
    }
}
