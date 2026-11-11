using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TextureRaw.Tests
{
    public class TextureRawDataTests
    {
        struct WriteBytes : IJob
        {
            public NativeArray<byte> Data;
            public void Execute() { for (int i = 0; i < Data.Length; i++) Data[i] = (byte)(i + 1); }
        }

        [Fact]
        public void CpuWitnessMatchesEveryNativeValue()
        {
            string cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
            string work = Environment.GetEnvironmentVariable("TEXTURE_RAW_UNIT_OUT") ?? Path.Combine(cache, "unbound/validation/runs/textureraw/unit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            string output = Path.Combine(work, "managed.jsonl");
            string previous = Environment.GetEnvironmentVariable("TEXTURE_RAW_PROBE_OUT");
            try
            {
                Environment.SetEnvironmentVariable("TEXTURE_RAW_PROBE_OUT", output);
                TextureRawProbe.Record();
            }
            finally { Environment.SetEnvironmentVariable("TEXTURE_RAW_PROBE_OUT", previous); }
            Dictionary<string, string> Load(string path) => File.ReadLines(path).Select(line => JsonSerializer.Deserialize<Dictionary<string, string>>(line)).ToDictionary(r => r["k"], r => r["v"]);
            var expected = Load(Path.Combine(AppContext.BaseDirectory, "unity-6000.0.71f1.jsonl"));
            var actual = Load(output);
            var missing = expected.Keys.Except(actual.Keys).OrderBy(k => k).Select(k => new { key = k, expected = expected[k] }).ToArray();
            var extra = actual.Keys.Except(expected.Keys).OrderBy(k => k).Select(k => new { key = k, actual = actual[k] }).ToArray();
            var failing = expected.Keys.Intersect(actual.Keys).Where(k => expected[k] != actual[k]).OrderBy(k => k).Select(k => new { key = k, expected = expected[k], actual = actual[k] }).ToArray();
            File.WriteAllText(Path.Combine(work, "complete-union.json"), JsonSerializer.Serialize(new { reference_count = expected.Count, candidate_count = actual.Count, missing, extra, failing }, new JsonSerializerOptions { WriteIndented = true }));
            Assert.True(missing.Length == 0 && extra.Length == 0 && failing.Length == 0,
                $"missing={missing.Length}, extra={extra.Length}, failing={failing.Length}; complete report: {work}/complete-union.json");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public unsafe void RawAndPixelViewsShareTheSameStoreRegardlessOfFirstGetter(bool rawFirst)
        {
            var texture = new Texture2D(4, 2, TextureFormat.RGBA32, true);
            try
            {
                if (rawFirst) texture.GetRawTextureData<byte>(); else texture.GetPixelData<byte>(0);
                var raw = texture.GetRawTextureData<byte>();
                var words = texture.GetRawTextureData<ushort>();
                var floats = texture.GetRawTextureData<float>();
                var triples = texture.GetRawTextureData<TextureRawProbe.ThreeBytes>();
                Assert.Equal(44, raw.Length);
                Assert.Equal(22, words.Length);
                Assert.Equal(11, floats.Length);
                Assert.Equal(14, triples.Length);
                Assert.Equal((IntPtr)raw.GetUnsafeReadOnlyPtr(), (IntPtr)words.GetUnsafeReadOnlyPtr());
                Assert.Equal((IntPtr)raw.GetUnsafeReadOnlyPtr(), (IntPtr)floats.GetUnsafeReadOnlyPtr());
                Assert.Equal((IntPtr)raw.GetUnsafeReadOnlyPtr(), (IntPtr)triples.GetUnsafeReadOnlyPtr());
                int offset = 0;
                for (int mip = 0; mip < texture.mipmapCount; mip++)
                {
                    var pixel = texture.GetPixelData<byte>(mip);
                    Assert.Equal((IntPtr)((byte*)raw.GetUnsafeReadOnlyPtr() + offset), (IntPtr)pixel.GetUnsafeReadOnlyPtr());
                    offset += pixel.Length;
                }
                words[0] = 0x1234;
                Assert.Equal((byte)0x34, raw[0]);
                Assert.Equal((byte)0x12, raw[1]);
                floats[1] = 0.5f;
                Assert.Equal(BitConverter.GetBytes(0.5f), raw.ToArray().Skip(4).Take(4));
                triples[0] = new TextureRawProbe.ThreeBytes { A = 3, B = 5, C = 7 };
                Assert.Equal(new byte[] { 3, 5, 7 }, raw.ToArray().Take(3));
                // jobs/Reference: Allocator.None disposal clears this view, not its owner.
                raw.Dispose();
                Assert.False(raw.IsCreated);
                Assert.Equal((ushort)0x0503, words[0]);
                Assert.Equal((byte)3, texture.GetPixelData<byte>(0)[0]);
            }
            finally { Object.DestroyImmediate(texture); }
        }

        /// <summary>The canonical unreadable.* rows: views handed out before the CPU data is
        /// removed stay valid until the texture ends, while new views are refused.</summary>
        [Fact]
        public void RemovingCpuDataKeepsBorrowedViewsAndRawGetterThrowsUnityException()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var second = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                var raw = texture.GetRawTextureData<byte>();
                var pixel = texture.GetPixelData<byte>(0);
                raw[5] = 77;
                texture.Apply(false, true);
                Assert.False(texture.isReadable);
                AtomicSafetyHandle.CheckReadAndThrow(NativeArrayUnsafeUtility.GetAtomicSafetyHandle(raw));
                AtomicSafetyHandle.CheckReadAndThrow(NativeArrayUnsafeUtility.GetAtomicSafetyHandle(pixel));
                Assert.Equal((byte)77, raw[5]);
                Assert.Equal((byte)77, pixel[5]);
                Assert.Throws<UnityException>(() => texture.GetRawTextureData<byte>());
                Assert.Throws<UnityException>(() => texture.GetRawTextureData<TextureRawProbe.ThreeBytes>());
                Assert.Equal(16, texture.GetRawTextureData().Length);
                // The kept views end with the texture.
                var kept = second.GetRawTextureData<byte>();
                second.Apply(false, true);
                Object.DestroyImmediate(second);
                Assert.Throws<ObjectDisposedException>(() => AtomicSafetyHandle.CheckReadAndThrow(NativeArrayUnsafeUtility.GetAtomicSafetyHandle(kept)));
            }
            finally
            {
                Object.DestroyImmediate(texture);
                if (second) Object.DestroyImmediate(second);
            }
        }

        [Fact]
        public void RepeatedGettersAllocateNoPixelCopies()
        {
            var texture = new Texture2D(64, 64, TextureFormat.RGBAFloat, true);
            try
            {
                texture.GetRawTextureData<byte>();
                texture.GetRawTextureData<float>();
                long before = GC.GetAllocatedBytesForCurrentThread();
                int lengths = 0;
                for (int i = 0; i < 100; i++)
                {
                    lengths += texture.GetRawTextureData<byte>().Length;
                    lengths += texture.GetRawTextureData<float>().Length;
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.True(lengths > 0);
                Assert.Equal(0, allocated);
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [Fact]
        public void RawWriterBlocksOtherViewsAndPixelApisUntilCompleted()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var raw = texture.GetRawTextureData<byte>();
            var pixel = texture.GetPixelData<byte>(0);
            var job = new WriteBytes { Data = raw }.Schedule();
            try
            {
                var reacquired = texture.GetRawTextureData<byte>();
                Assert.Throws<InvalidOperationException>(() => reacquired[0]);
                Assert.Throws<InvalidOperationException>(() => pixel[0]);
                Assert.Throws<InvalidOperationException>(() => texture.GetPixels());
                Assert.Throws<InvalidOperationException>(() => texture.SetPixel(0, 0, Color.red));
                job.Complete();
                Assert.Equal((byte)1, pixel[0]);
                Assert.Equal((byte)16, raw[15]);
            }
            finally { job.Complete(); Object.DestroyImmediate(texture); }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OwnerCompletesRawJobsBeforeReleasingThePin(bool destroy)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var raw = texture.GetRawTextureData<byte>();
            var job = new WriteBytes { Data = raw }.Schedule();
            try
            {
                if (destroy) Object.DestroyImmediate(texture);
                else Assert.True(texture.Reinitialize(4, 4));
                Assert.True(job.IsCompleted);
                job.Complete();
                Assert.Throws<ObjectDisposedException>(() => raw[0]);
                if (!destroy) Assert.Equal(64, texture.GetRawTextureData<byte>().Length);
            }
            finally { job.Complete(); if (!destroy) Object.DestroyImmediate(texture); }
        }
    }
}
