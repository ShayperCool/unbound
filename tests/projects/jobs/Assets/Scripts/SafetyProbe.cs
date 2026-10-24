using System;
using System.Collections;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

/// <summary>
/// The job safety system with ENABLE_UNITY_COLLECTIONS_CHECKS (the Editor's default): what throws,
/// when and with which message.
/// </summary>
public static unsafe class SafetyProbe
{
    public struct WriteJob : IJob
    {
        public NativeArray<int> Data;

        public void Execute()
        {
            for (int i = 0; i < Data.Length; i++) Data[i] = i;
        }
    }

    public struct ReadJob : IJob
    {
        [ReadOnly] public NativeArray<int> Data;
        public NativeArray<int> Sum;

        public void Execute()
        {
            int sum = 0;
            for (int i = 0; i < Data.Length; i++) sum += Data[i];
            Sum[0] = sum;
        }
    }

    public struct ReadOnlyWriter : IJob
    {
        [ReadOnly] public NativeArray<int> Data;

        public void Execute() => Data[0] = 1;
    }

    public struct WriteOnlyReader : IJob
    {
        [WriteOnly] public NativeArray<int> Data;
        public NativeArray<int> Out;

        public void Execute() => Out[0] = Data[0];
    }

    public struct NeighbourWriter : IJobParallelFor
    {
        public NativeArray<int> Data;

        public void Execute(int index) => Data[(index + 1) % Data.Length] = index;
    }

    public struct NeighbourReader : IJobParallelFor
    {
        public NativeArray<int> Data;

        public void Execute(int index) => Data[index] = Data[(index + 1) % Data.Length];
    }

    public struct UnrestrictedWriter : IJobParallelFor
    {
        [NativeDisableParallelForRestriction] public NativeArray<int> Data;

        public void Execute(int index) => Data[(index + 1) % Data.Length] = index;
    }

    public struct ManagedFieldJob : IJob
    {
        public string Name;

        public void Execute() { }
    }

    public struct PointerJob : IJob
    {
        public int* Pointer;

        public void Execute() { }
    }

    public struct AllowedPointerJob : IJob
    {
        [NativeDisableUnsafePtrRestriction] public int* Pointer;

        public void Execute() => *Pointer = 42;
    }

    public struct AllocatingJob : IJob
    {
        public Allocator Allocator;
        public NativeArray<int> Out;

        public void Execute()
        {
            var array = new NativeArray<int>(4, Allocator);
            Out[0] = array.Length;
            array.Dispose();
        }
    }

    public struct DisposingJob : IJob
    {
        public NativeArray<int> Data;

        public void Execute() => Data.Dispose();
    }

    public struct DeallocateJob : IJob
    {
        [DeallocateOnJobCompletion] public NativeArray<int> Data;
        public NativeArray<int> Out;

        public void Execute() => Out[0] = Data[0];
    }

    public struct UnsafeJob : IJob
    {
        [NativeDisableContainerSafetyRestriction] public NativeArray<int> Data;

        public void Execute() => Data[0] = 7;
    }

    public struct NestedJob : IJob
    {
        public Inner Payload;

        public void Execute() => Payload.Values[0] = Payload.Scale;
    }

    public struct Inner
    {
        public NativeArray<int> Values;
        public int Scale;
    }

    public static void RunAll()
    {
        MainThreadAccess();
        Disposal();
        Scheduling();
        InJob();
        ParallelFor();
        Handles();
    }

    static void MainThreadAccess()
    {
        ProbeLog.Begin("safety.writer");
        var data = new NativeArray<int>(16, Allocator.TempJob);
        var handle = new WriteJob { Data = data }.Schedule();
        ProbeLog.Try("read", () => { var _ = data[0]; });
        ProbeLog.Try("write", () => data[0] = 5);
        ProbeLog.Try("length", () => { var _ = data.Length; });
        ProbeLog.Try("dispose", () => data.Dispose());
        ProbeLog.Try("toArray", () => data.ToArray());
        ProbeLog.Try("getSubArray", () => data.GetSubArray(0, 4));
        ProbeLog.Try("subArrayRead", () => { var _ = data.GetSubArray(0, 4)[0]; });
        ProbeLog.Try("enumerate", () => { foreach (var v in data) { } });
        ProbeLog.Try("asReadOnlyRead", () => { var _ = data.AsReadOnly()[0]; });
        ProbeLog.Try("unsafePtr", () => data.GetUnsafePtr());
        ProbeLog.Try("unsafeReadOnlyPtr", () => data.GetUnsafeReadOnlyPtr());
        ProbeLog.Try("bufferWithoutChecks", () => NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(data));
        // Finished but not completed: the safety system waits for a sync point.
        while (!handle.IsCompleted) System.Threading.Thread.Sleep(1);
        ProbeLog.Try("readAfterIsCompleted", () => { var _ = data[0]; });
        handle.Complete();
        ProbeLog.Try("readAfterComplete", () => { var _ = data[3]; });
        ProbeLog.Value("value", data[3]);
        data.Dispose();

        ProbeLog.Begin("safety.reader");
        data = new NativeArray<int>(16, Allocator.TempJob);
        var sum = new NativeArray<int>(1, Allocator.TempJob);
        handle = new ReadJob { Data = data, Sum = sum }.Schedule();
        ProbeLog.Try("read", () => { var _ = data[0]; });
        ProbeLog.Try("write", () => data[0] = 5);
        ProbeLog.Try("dispose", () => data.Dispose());
        ProbeLog.Try("readSum", () => { var _ = sum[0]; });
        ProbeLog.Try("copyFrom", () => data.CopyFrom(new int[16]));
        ProbeLog.Try("copyTo", () => data.CopyTo(new int[16]));
        handle.Complete();
        ProbeLog.Try("writeAfterComplete", () => data[0] = 5);
        data.Dispose();
        sum.Dispose();
    }

    static void Disposal()
    {
        ProbeLog.Begin("safety.disposed");
        var data = new NativeArray<int>(4, Allocator.Persistent);
        var copy = data;
        var sub = data.GetSubArray(1, 2);
        var readOnly = data.AsReadOnly();
        data.Dispose();
        ProbeLog.Value("isCreated", data.IsCreated);
        ProbeLog.Value("copy.isCreated", copy.IsCreated);
        ProbeLog.Value("length", data.Length);
        ProbeLog.Try("read", () => { var _ = data[0]; });
        ProbeLog.Try("copy.read", () => { var _ = copy[0]; });
        ProbeLog.Try("copy.write", () => copy[0] = 1);
        ProbeLog.Try("copy.length", () => { var _ = copy.Length; });
        ProbeLog.Try("sub.read", () => { var _ = sub[0]; });
        ProbeLog.Try("readOnly.read", () => { var _ = readOnly[0]; });
        ProbeLog.Try("dispose", () => data.Dispose());
        ProbeLog.Try("copy.dispose", () => copy.Dispose());
        ProbeLog.Try("copy.toArray", () => copy.ToArray());
        ProbeLog.Try("copy.disposeJob", () => copy.Dispose(default));
        ProbeLog.Try("default.dispose", () => default(NativeArray<int>).Dispose());
        ProbeLog.Try("default.read", () => { var _ = default(NativeArray<int>)[0]; });
        ProbeLog.Try("sub.dispose", () => data.GetSubArray(0, 1).Dispose());

        ProbeLog.Begin("safety.allocation");
        ProbeLog.Try("none", () => new NativeArray<int>(1, Allocator.None));
        ProbeLog.Try("invalid", () => new NativeArray<int>(1, Allocator.Invalid));
        ProbeLog.Try("negative", () => new NativeArray<int>(-1, Allocator.Temp));
        ProbeLog.Try("custom", () => new NativeArray<int>(1, (Allocator)70));
        ProbeLog.Try("managedElement", () => new NativeArray<Probe>(1, Allocator.Temp));
        ProbeLog.Try("copyFromNull", () => new NativeArray<int>(null, Allocator.Temp));
        var temp = new NativeArray<int>(3, Allocator.Temp);
        ProbeLog.Try("subArrayOutOfRange", () => temp.GetSubArray(2, 3));
        ProbeLog.Try("indexOutOfRange", () => { var _ = temp[3]; });
        ProbeLog.Try("negativeIndex", () => { var _ = temp[-1]; });
        ProbeLog.Try("copyLengths", () => NativeArray<int>.Copy(temp, new int[2]));
        ProbeLog.Try("copyRange", () => NativeArray<int>.Copy(temp, 2, new int[5], 0, 3));
        ProbeLog.Try("reinterpretSize", () => temp.Reinterpret<long>());
        ProbeLog.Try("reinterpretExpected", () => temp.Reinterpret<short>(4));
        ProbeLog.Try("reinterpretLoad", () => temp.ReinterpretLoad<long>(2));
        ProbeLog.Try("tempDispose", () => temp.Dispose());
        ProbeLog.Try("tempDisposeTwice", () => temp.Dispose());

        ProbeLog.Begin("safety.temp");
        var a = new NativeArray<int>(2, Allocator.Temp);
        var aCopy = a;
        var b = new NativeArray<int>(2, Allocator.Temp);
        var h1 = AtomicSafetyHandle.GetTempMemoryHandle();
        var h2 = AtomicSafetyHandle.GetTempMemoryHandle();
        ProbeLog.Value("aHandleIsTemp", AtomicSafetyHandle.IsTempMemoryHandle(NativeArrayUnsafeUtility.GetAtomicSafetyHandle(a)));
        a.Dispose();
        ProbeLog.Try("aCopy.read", () => { var _ = aCopy[0]; });
        ProbeLog.Try("aCopy.dispose", () => aCopy.Dispose());
        ProbeLog.Try("b.read", () => { var _ = b[0]; });
        ProbeLog.Value("h1.valid", AtomicSafetyHandle.IsHandleValid(h1));
        AtomicSafetyHandle.Release(h2);
        ProbeLog.Value("h1.validAfterReleaseH2", AtomicSafetyHandle.IsHandleValid(h1));
        ProbeLog.Value("h2.validAfterReleaseH2", AtomicSafetyHandle.IsHandleValid(h2));
        ProbeLog.Try("b.readAfterReleaseH2", () => { var _ = b[0]; });
        var h3 = AtomicSafetyHandle.GetTempMemoryHandle();
        ProbeLog.Value("h3.valid", AtomicSafetyHandle.IsHandleValid(h3));
        var c = new NativeArray<int>(2, Allocator.Temp);
        ProbeLog.Try("c.read", () => { var _ = c[0]; });
        c.Dispose();

        ProbeLog.Begin("safety.existingData");
        int* memory = stackalloc int[4];
        var existing = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<int>(memory, 4, Allocator.None);
        ProbeLog.Value("isDefaultHandle", AtomicSafetyHandle.IsDefaultValue(NativeArrayUnsafeUtility.GetAtomicSafetyHandle(existing)));
        ProbeLog.Try("readWithoutHandle", () => { var _ = existing[0]; });
        NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref existing, AtomicSafetyHandle.Create());
        ProbeLog.Try("readWithHandle", () => { var _ = existing[0]; });
        ProbeLog.Try("disposeNone", () => existing.Dispose());
        ProbeLog.Value("disposeNone.isCreated", existing.IsCreated);

        ProbeLog.Begin("safety.handles");
        var safety = AtomicSafetyHandle.Create();
        ProbeLog.Value("valid", AtomicSafetyHandle.IsHandleValid(safety));
        ProbeLog.Value("validNonDefault", AtomicSafetyHandle.IsValidNonDefaultHandle(safety));
        ProbeLog.Value("isTemp", AtomicSafetyHandle.IsTempMemoryHandle(safety));
        ProbeLog.Value("allowReadOrWrite", AtomicSafetyHandle.GetAllowReadOrWriteAccess(safety));
        ProbeLog.Value("nested", AtomicSafetyHandle.GetNestedContainer(safety));
        ProbeLog.Try("checkRead", () => AtomicSafetyHandle.CheckReadAndThrow(safety));
        ProbeLog.Try("checkWrite", () => AtomicSafetyHandle.CheckWriteAndThrow(safety));
        ProbeLog.Try("checkDeallocate", () => AtomicSafetyHandle.CheckDeallocateAndThrow(safety));
        ProbeLog.Value("enforce", AtomicSafetyHandle.EnforceAllBufferJobsHaveCompleted(safety).ToString());
        AtomicSafetyHandle.SetAllowReadOrWriteAccess(safety, false);
        ProbeLog.Try("checkReadDisallowed", () => AtomicSafetyHandle.CheckReadAndThrow(safety));
        ProbeLog.Try("checkWriteDisallowed", () => AtomicSafetyHandle.CheckWriteAndThrow(safety));
        ProbeLog.Try("checkExistsDisallowed", () => AtomicSafetyHandle.CheckExistsAndThrow(safety));
        AtomicSafetyHandle.SetAllowReadOrWriteAccess(safety, true);
        var secondary = safety;
        AtomicSafetyHandle.UseSecondaryVersion(ref secondary);
        ProbeLog.Try("secondary.read", () => AtomicSafetyHandle.CheckReadAndThrow(secondary));
        ProbeLog.Try("secondary.write", () => AtomicSafetyHandle.CheckWriteAndThrow(secondary));
        AtomicSafetyHandle.SetAllowSecondaryVersionWriting(safety, false);
        ProbeLog.Try("secondary.writeDisallowed", () => AtomicSafetyHandle.CheckWriteAndThrow(secondary));
        ProbeLog.Try("primary.writeWhenSecondaryDisallowed", () => AtomicSafetyHandle.CheckWriteAndThrow(safety));
        AtomicSafetyHandle.SetAllowSecondaryVersionWriting(safety, true);
        ProbeLog.Try("bump", () => AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(safety));
        ProbeLog.Try("secondary.readAfterBump", () => AtomicSafetyHandle.CheckReadAndThrow(secondary));
        ProbeLog.Try("secondary.existsAfterBump", () => AtomicSafetyHandle.CheckExistsAndThrow(secondary));
        ProbeLog.Value("secondary.validAfterBump", AtomicSafetyHandle.IsHandleValid(secondary));
        ProbeLog.Try("primary.readAfterBump", () => AtomicSafetyHandle.CheckReadAndThrow(safety));
        ProbeLog.Try("getSecondaryPointer", () => AtomicSafetyHandle.CheckGetSecondaryDataPointerAndThrow(safety));
        var copyOfSafety = safety;
        AtomicSafetyHandle.Release(safety);
        ProbeLog.Value("released.valid", AtomicSafetyHandle.IsHandleValid(copyOfSafety));
        ProbeLog.Try("released.read", () => AtomicSafetyHandle.CheckReadAndThrow(copyOfSafety));
        ProbeLog.Try("released.write", () => AtomicSafetyHandle.CheckWriteAndThrow(copyOfSafety));
        ProbeLog.Try("released.exists", () => AtomicSafetyHandle.CheckExistsAndThrow(copyOfSafety));
        ProbeLog.Try("released.deallocate", () => AtomicSafetyHandle.CheckDeallocateAndThrow(copyOfSafety));
        ProbeLog.Try("released.release", () => AtomicSafetyHandle.Release(copyOfSafety));
        ProbeLog.Value("released.enforce", AtomicSafetyHandle.EnforceAllBufferJobsHaveCompleted(copyOfSafety).ToString());
        ProbeLog.Value("default.isDefault", AtomicSafetyHandle.IsDefaultValue(default(AtomicSafetyHandle)));
        ProbeLog.Value("default.validNonDefault", AtomicSafetyHandle.IsValidNonDefaultHandle(default(AtomicSafetyHandle)));
        ProbeLog.Try("default.validate", () => AtomicSafetyHandle.ValidateNonDefaultHandle(default(AtomicSafetyHandle)));
        var temp2 = AtomicSafetyHandle.GetTempMemoryHandle();
        ProbeLog.Value("temp.isTemp", AtomicSafetyHandle.IsTempMemoryHandle(temp2));
        ProbeLog.Value("temp.valid", AtomicSafetyHandle.IsHandleValid(temp2));
        ProbeLog.Try("temp.deallocate", () => AtomicSafetyHandle.CheckDeallocateAndThrow(temp2));
        ProbeLog.Try("temp.release", () => AtomicSafetyHandle.Release(temp2));
        ProbeLog.Value("temp.validAfterRelease", AtomicSafetyHandle.IsHandleValid(temp2));
        var slice = AtomicSafetyHandle.GetTempUnsafePtrSliceHandle();
        ProbeLog.Value("slice.valid", AtomicSafetyHandle.IsHandleValid(slice));
        ProbeLog.Value("slice.isTemp", AtomicSafetyHandle.IsTempMemoryHandle(slice));
        ProbeLog.Try("slice.write", () => AtomicSafetyHandle.CheckWriteAndThrow(slice));
        ProbeLog.Try("slice.deallocate", () => AtomicSafetyHandle.CheckDeallocateAndThrow(slice));
        var undisposable = AtomicSafetyHandle.Create();
        AtomicSafetyHandle.PrepareUndisposable(ref undisposable);
        ProbeLog.Try("undisposable.deallocate", () => AtomicSafetyHandle.CheckDeallocateAndThrow(undisposable));
        ProbeLog.Try("undisposable.write", () => AtomicSafetyHandle.CheckWriteAndThrow(undisposable));
        int id = AtomicSafetyHandle.NewStaticSafetyId<NativeArray<float>>();
        ProbeLog.Value("staticSafetyIdPositive", id > 0);
        ProbeLog.Value("staticSafetyIdNew", AtomicSafetyHandle.NewStaticSafetyId<NativeArray<float>>() != id);
        var custom = AtomicSafetyHandle.Create();
        var message = System.Text.Encoding.UTF8.GetBytes("custom deallocated message");
        fixed (byte* bytes = message)
            AtomicSafetyHandle.SetCustomErrorMessage(id, AtomicSafetyErrorType.Deallocated, bytes, message.Length);
        AtomicSafetyHandle.SetStaticSafetyId(ref custom, id);
        var customCopy = custom;
        AtomicSafetyHandle.Release(custom);
        ProbeLog.Try("custom.read", () => AtomicSafetyHandle.CheckReadAndThrow(customCopy));
        ProbeLog.Try("custom.exists", () => AtomicSafetyHandle.CheckExistsAndThrow(customCopy));
    }

    static void Scheduling()
    {
        ProbeLog.Begin("safety.schedule");
        var data = new NativeArray<int>(8, Allocator.TempJob);
        var sum = new NativeArray<int>(1, Allocator.TempJob);
        var first = new WriteJob { Data = data }.Schedule();
        ProbeLog.Try("writerAfterWriter", () => new WriteJob { Data = data }.Schedule().Complete());
        ProbeLog.Try("readerAfterWriter", () => new ReadJob { Data = data, Sum = sum }.Schedule().Complete());
        var second = default(JobHandle);
        ProbeLog.Try("writerWithDependency", () => second = new WriteJob { Data = data }.Schedule(first));
        second.Complete();
        first.Complete();

        var r1 = new ReadJob { Data = data, Sum = sum }.Schedule();
        var sum2 = new NativeArray<int>(1, Allocator.TempJob);
        var r2 = default(JobHandle);
        ProbeLog.Try("readerAfterReader", () => r2 = new ReadJob { Data = data, Sum = sum2 }.Schedule());
        ProbeLog.Try("writerAfterReaders", () => new WriteJob { Data = data }.Schedule().Complete());
        ProbeLog.Try("writerAfterOneReader", () => new WriteJob { Data = data }.Schedule(r1).Complete());
        var w = default(JobHandle);
        ProbeLog.Try("writerAfterBothReaders", () => w = new WriteJob { Data = data }.Schedule(JobHandle.CombineDependencies(r1, r2)));
        w.Complete();
        r1.Complete();
        r2.Complete();
        sum2.Dispose();

        ProbeLog.Try("sameContainerTwice", () => new CopyJob2 { A = data, B = data }.Schedule().Complete());
        ProbeLog.Try("sameContainerReadTwice", () => new ReadTwiceJob { A = data, B = data }.Schedule().Complete());
        ProbeLog.Try("managedField", () => new ManagedFieldJob { Name = "x" }.Schedule().Complete());
        ProbeLog.Try("managedFieldNull", () => new ManagedFieldJob().Schedule().Complete());
        int* valuePtr = (int*)UnsafeUtility.Malloc(4, 4, Allocator.Persistent);
        *valuePtr = 0;
        ProbeLog.Try("pointer", () => new PointerJob { Pointer = valuePtr }.Schedule().Complete());
        ProbeLog.Try("pointerNull", () => new PointerJob().Schedule().Complete());
        ProbeLog.Try("allowedPointer", () => new AllowedPointerJob { Pointer = valuePtr }.Schedule().Complete());
        ProbeLog.Value("allowedPointer.value", *valuePtr);
        UnsafeUtility.Free(valuePtr, Allocator.Persistent);
        ProbeLog.Try("nested", () => new NestedJob { Payload = new Inner { Values = data, Scale = 3 } }.Schedule().Complete());
        ProbeLog.Value("nested.value", data[0]);

        var temp = new NativeArray<int>(4, Allocator.Temp);
        ProbeLog.Try("tempInJob", () => new WriteJob { Data = temp }.Schedule().Complete());
        ProbeLog.Try("tempInRun", () => new WriteJob { Data = temp }.Run());
        temp.Dispose();

        var disposed = new NativeArray<int>(4, Allocator.TempJob);
        disposed.Dispose();
        ProbeLog.Try("disposedInJob", () => new WriteJob { Data = disposed }.Schedule().Complete());
        ProbeLog.Try("disposedInRun", () => new WriteJob { Data = disposed }.Run());
        ProbeLog.Try("defaultInJob", () => new WriteJob().Schedule().Complete());

        var unsafeHandle = default(JobHandle);
        var plain = new WriteJob { Data = data }.Schedule();
        ProbeLog.Try("disableSafety", () => unsafeHandle = new UnsafeJob { Data = data }.Schedule());
        unsafeHandle.Complete();
        plain.Complete();

        var deallocated = new NativeArray<int>(4, Allocator.TempJob);
        deallocated[0] = 9;
        var outArray = new NativeArray<int>(1, Allocator.TempJob);
        var copy = deallocated;
        new DeallocateJob { Data = deallocated, Out = outArray }.Schedule().Complete();
        ProbeLog.Value("deallocateOnCompletion.out", outArray[0]);
        ProbeLog.Value("deallocateOnCompletion.isCreated", copy.IsCreated);
        ProbeLog.Try("deallocateOnCompletion.read", () => { var _ = copy[0]; });
        ProbeLog.Try("deallocateOnCompletion.dispose", () => copy.Dispose());

        var tempJob = new NativeArray<int>(4, Allocator.TempJob);
        var disposeHandle = tempJob.Dispose(default);
        ProbeLog.Value("disposeJob.isCreated", tempJob.IsCreated);
        ProbeLog.Try("disposeJob.read", () => { var _ = tempJob[0]; });
        disposeHandle.Complete();

        var before = new NativeArray<int>(4, Allocator.TempJob);
        var writer = new WriteJob { Data = before }.Schedule();
        var afterDispose = default(JobHandle);
        ProbeLog.Try("disposeJobWithoutDependency", () => afterDispose = before.Dispose(default));
        ProbeLog.Try("disposeJobWithDependency", () => afterDispose = before.Dispose(writer));
        afterDispose.Complete();
        writer.Complete();

        outArray.Dispose();
        data.Dispose();
        sum.Dispose();
    }

    public struct CopyJob2 : IJob
    {
        public NativeArray<int> A;
        public NativeArray<int> B;

        public void Execute() => B[0] = A[1];
    }

    public struct ReadTwiceJob : IJob
    {
        [ReadOnly] public NativeArray<int> A;
        [ReadOnly] public NativeArray<int> B;

        public void Execute() { var _ = A[0] + B[0]; }
    }

    static void InJob()
    {
        ProbeLog.Begin("safety.injob");
        var data = new NativeArray<int>(4, Allocator.TempJob);
        var output = new NativeArray<int>(1, Allocator.TempJob);
        using (var capture = new LogCapture())
        {
            new ReadOnlyWriter { Data = data }.Schedule().Complete();
            capture.Record("readOnlyWrite");
        }
        using (var capture = new LogCapture())
        {
            ProbeLog.Try("readOnlyWrite.run", () => new ReadOnlyWriter { Data = data }.Run());
            capture.Record("readOnlyWrite.run");
        }
        using (var capture = new LogCapture())
        {
            new WriteOnlyReader { Data = data, Out = output }.Schedule().Complete();
            capture.Record("writeOnlyRead");
        }
        using (var capture = new LogCapture())
        {
            new AllocatingJob { Allocator = Allocator.TempJob, Out = output }.Schedule().Complete();
            capture.Record("allocateTempJob");
        }
        using (var capture = new LogCapture())
        {
            output[0] = 0;
            new AllocatingJob { Allocator = Allocator.Temp, Out = output }.Schedule().Complete();
            ProbeLog.Value("allocateTemp.out", output[0]);
            capture.Record("allocateTemp");
        }
        using (var capture = new LogCapture())
        {
            new AllocatingJob { Allocator = Allocator.Persistent, Out = output }.Schedule().Complete();
            capture.Record("allocatePersistent");
        }
        var victim = new NativeArray<int>(4, Allocator.TempJob);
        using (var capture = new LogCapture())
        {
            new DisposingJob { Data = victim }.Schedule().Complete();
            capture.Record("disposeInJob");
        }
        ProbeLog.Value("disposeInJob.isCreated", victim.IsCreated);
        ProbeLog.Try("disposeInJob.disposeAfter", () => victim.Dispose());
        data.Dispose();
        output.Dispose();
    }

    static void ParallelFor()
    {
        ProbeLog.Begin("safety.parallelfor");
        var data = new NativeArray<int>(64, Allocator.TempJob);
        using (var capture = new LogCapture())
        {
            new NeighbourWriter { Data = data }.Schedule(data.Length, 8).Complete();
            var messages = capture.Messages;
            ProbeLog.Value("neighbourWrite.logged", messages.Count > 0);
            ProbeLog.Value("neighbourWrite.first", messages.Count > 0 ? Normalize(messages[0]) : "");
        }
        using (var capture = new LogCapture())
        {
            new NeighbourReader { Data = data }.Schedule(data.Length, 64).Complete();
            var messages = capture.Messages;
            ProbeLog.Value("neighbourRead.count", messages.Count);
            ProbeLog.Value("neighbourRead.first", messages.Count > 0 ? messages[0] : "");
        }
        using (var capture = new LogCapture())
        {
            new UnrestrictedWriter { Data = data }.Schedule(data.Length, 8).Complete();
            capture.Record("unrestricted");
        }
        using (var capture = new LogCapture())
        {
            new NeighbourWriter { Data = data }.Run(data.Length);
            var messages = capture.Messages;
            ProbeLog.Value("neighbourWrite.run.count", messages.Count);
        }
        data.Dispose();
    }

    /// <summary>The restricted range in the message depends on which batch a worker took first.</summary>
    static string Normalize(string message)
    {
        int open = message.IndexOf('['), close = message.IndexOf(']');
        if (open < 0 || close < open) return message;
        var index = message.IndexOf("Index ", StringComparison.Ordinal);
        var head = index >= 0 ? "Index N" + message.Substring(message.IndexOf(' ', index + 6)) : message;
        open = head.IndexOf('[');
        close = head.IndexOf(']');
        return head.Substring(0, open) + "[A...B]" + head.Substring(close + 1);
    }

    static void Handles()
    {
        ProbeLog.Begin("safety.leaks");
        ProbeLog.Value("mode", NativeLeakDetection.Mode.ToString());
        ProbeLog.Try("setInvalid", () => NativeLeakDetection.Mode = (NativeLeakDetectionMode)0);
        var mode = NativeLeakDetection.Mode;
        NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;
        ProbeLog.Value("setStackTrace", NativeLeakDetection.Mode.ToString());
        NativeLeakDetection.Mode = mode;
    }

    static NativeArray<int> s_FrameTemp;
    static NativeArray<int> s_FramePersistent;

    public static IEnumerator AcrossFrames()
    {
        ProbeLog.Begin("safety.frames");
        s_FrameTemp = new NativeArray<int>(4, Allocator.Temp);
        s_FrameTemp[0] = 3;
        var handle = AtomicSafetyHandle.GetTempMemoryHandle();
        ProbeLog.Value("sameFrame.read", s_FrameTemp[0]);
        yield return null;
        ProbeLog.Value("nextFrame.tempHandleValid", AtomicSafetyHandle.IsHandleValid(handle));
        ProbeLog.Try("nextFrame.read", () => { var _ = s_FrameTemp[0]; });
        ProbeLog.Try("nextFrame.dispose", () => s_FrameTemp.Dispose());
        var fresh = AtomicSafetyHandle.GetTempMemoryHandle();
        ProbeLog.Value("nextFrame.newTempHandleValid", AtomicSafetyHandle.IsHandleValid(fresh));

        s_FramePersistent = new NativeArray<int>(4, Allocator.TempJob);
        var pending = new WriteJob { Data = s_FramePersistent }.Schedule();
        yield return null;
        ProbeLog.Try("pendingNextFrame.read", () => { var _ = s_FramePersistent[0]; });
        pending.Complete();
        s_FramePersistent.Dispose();
    }

    struct Probe
    {
        public string Name;
    }
}
