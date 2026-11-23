using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    /// <summary>
    /// GPU readbacks as the Editor completes them (tests/projects/gpubuffers): the data is the
    /// source's when the request is made (Unbound reads it then, the renderer running on the
    /// game's thread), but a request is done only at the next frame's
    /// <c>EarlyUpdate.UpdateAsyncReadbackManager</c>, which runs the callbacks in request order;
    /// the Editor completed them one or two frames later, as its GPU finished. The data stays
    /// available until the second readback update that sees the request done (the next frame's
    /// for one completed by the manager, the one after for one a wait completed), after which the
    /// request reads done with an error. A request that fails its checks logs, is done
    /// with an error and calls back before <c>Request</c> returns. <c>WaitForCompletion</c> and
    /// <c>WaitAllRequests</c> complete at once and call back before they return; <c>Update</c>
    /// completes nothing early. A native array a request fills cannot be accessed until it is done.
    /// </summary>
    internal static class AsyncReadbacks
    {
        internal sealed class Result
        {
            internal byte[][] Layers = Array.Empty<byte[]>();
            internal int Width, Height, Depth;
            internal bool HasError, Done;
            /// <summary>The frame it completes on, then the frame it completed on.</summary>
            internal int Frame;
            /// <summary>The frame whose readback update first saw it done (-1: none yet).</summary>
            internal int Seen = -1;
            internal Action<UnityEngine.Rendering.AsyncGPUReadbackRequest> Callback;
            /// <summary>The native array the data goes into on completion, locked until then.</summary>
            internal unsafe void* Output;
            internal long OutputLength;
            internal Safety.ExternalWriteReservation OutputReservation;
            internal bool HasOutput;
        }

        static readonly Dictionary<long, Result> s_Live = new Dictionary<long, Result>();
        static readonly List<long> s_Pending = new List<long>();
        static readonly List<long> s_Completed = new List<long>();
        static long s_Next;
        static bool s_Registered;

        internal static Result Find(IntPtr id) => s_Live.TryGetValue((long)id, out var r) ? r : null;

        /// <summary>A failed request: logged, done with an error, called back now.</summary>
        internal static UnityEngine.Rendering.AsyncGPUReadbackRequest Fail(string message, Action<UnityEngine.Rendering.AsyncGPUReadbackRequest> callback)
        {
            if (message != null) Debug.LogError(message);
            var request = Make(new Result { HasError = true, Done = true, Frame = Time.frameCount }, pending: false);
            Call(callback, request);
            return request;
        }

        /// <summary>A request whose data was read now; it completes at the next frame's update.</summary>
        internal static UnityEngine.Rendering.AsyncGPUReadbackRequest Start(Result result)
        {
            if (result.HasError)
            {
                result.Done = true;
                result.Frame = Time.frameCount;
                var failed = Make(result, pending: false);
                Call(result.Callback, failed);
                return failed;
            }
            result.Frame = Time.frameCount + 1;
            return Make(result, pending: true);
        }

        static UnityEngine.Rendering.AsyncGPUReadbackRequest Make(Result result, bool pending)
        {
            if (!s_Registered)
            {
                s_Registered = true;
                PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.EarlyUpdate.UpdateAsyncReadbackManager), Update);
            }
            long id = ++s_Next;
            s_Live[id] = result;
            if (pending) s_Pending.Add(id);
            else s_Completed.Add(id);
            return new UnityEngine.Rendering.AsyncGPUReadbackRequest { m_Ptr = (IntPtr)id, m_Version = 1 };
        }

        static void Call(Action<UnityEngine.Rendering.AsyncGPUReadbackRequest> callback, UnityEngine.Rendering.AsyncGPUReadbackRequest request)
        {
            if (callback == null) return;
            try { callback(request); }
            catch (Exception e) { Debug.LogException(e); }
        }

        static unsafe void Complete(long id, bool update = false)
        {
            if (!s_Live.TryGetValue(id, out var result) || result.Done) return;
            s_Pending.Remove(id);
            result.Done = true;
            result.Frame = Time.frameCount;
            if (update) result.Seen = Time.frameCount;
            if (result.HasOutput)
            {
                try
                {
                    long at = 0;
                    foreach (var layer in result.Layers)
                    {
                        long n = Math.Min(layer.Length, result.OutputLength - at);
                        if (n <= 0) break;
                        fixed (byte* p = layer) UnsafeUtility.MemCpy((byte*)result.Output + at, p, n);
                        at += n;
                    }
                }
                finally
                {
                    result.OutputReservation.Dispose();
                    result.OutputReservation = null;
                    result.HasOutput = false;
                    result.Output = null;
                }
            }
            s_Completed.Add(id);
            Call(result.Callback, new UnityEngine.Rendering.AsyncGPUReadbackRequest { m_Ptr = (IntPtr)id, m_Version = 1 });
        }

        /// <summary>EarlyUpdate.UpdateAsyncReadbackManager: releases what completed in an earlier
        /// frame, then completes the requests due.</summary>
        static void Update()
        {
            int frame = Time.frameCount;
            for (int i = s_Completed.Count - 1; i >= 0; i--)
            {
                long id = s_Completed[i];
                if (s_Live.TryGetValue(id, out var r) && (r.Seen < 0 || r.Seen >= frame))
                {
                    if (r.Seen < 0) r.Seen = frame;
                    continue;
                }
                s_Live.Remove(id);
                s_Completed.RemoveAt(i);
            }
            if (s_Pending.Count == 0) return;
            foreach (var id in s_Pending.ToArray())
                if (s_Live.TryGetValue(id, out var r) && r.Frame <= frame) Complete(id, update: true);
        }

        internal static void Wait(IntPtr id) => Complete((long)id);

        internal static void WaitAll()
        {
            foreach (var id in s_Pending.ToArray()) Complete(id);
        }

        /// <summary>Locks <paramref name="output"/> until the request is done, then copies the data
        /// into it; a smaller array fails the request.</summary>
        internal static unsafe UnityEngine.Rendering.AsyncGPUReadbackRequest Into<T>(ref NativeArray<T> output, Result result) where T : struct
        {
            long length = (long)output.Length * UnsafeUtility.SizeOf<T>();
            if (!result.HasError)
            {
                if (output.m_AllocatorLabel != Allocator.Persistent && output.m_AllocatorLabel != Allocator.TempJob)
                    throw new ArgumentException("Unbound: a GPU readback destination must own Persistent or TempJob memory; frame-local and borrowed storage are not supported.", nameof(output));
                long size = 0;
                foreach (var layer in result.Layers) size += layer.Length;
                if (size > length)
                    return Fail($"Size of source buffer data ({size} bytes) is larger than the destination nativeArray ({length} bytes).", result.Callback);
                result.OutputReservation = Safety.ReserveExternalWrite(output.m_Safety);
                result.Output = NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(output);
                result.OutputLength = length;
                result.HasOutput = true;
            }
            return Start(result);
        }

        /// <summary>As <see cref="Into{T}(ref NativeArray{T}, Result)"/>, into a slice: the data
        /// goes contiguously from the slice's first element, a strided slice's too (measured,
        /// tests/projects/cmdbuffers slice.strided), and fits in its length in elements.</summary>
        internal static unsafe UnityEngine.Rendering.AsyncGPUReadbackRequest Into<T>(ref NativeSlice<T> output, Result result) where T : struct
        {
            long length = (long)output.Length * UnsafeUtility.SizeOf<T>();
            if (!result.HasError)
            {
                long size = 0;
                foreach (var layer in result.Layers) size += layer.Length;
                if (size > length)
                    return Fail($"Size of source buffer data ({size} bytes) is larger than the destination nativeArray ({length} bytes).", result.Callback);
                result.OutputReservation = Safety.ReserveExternalWrite(output.m_Safety);
                result.Output = output.m_Buffer;
                result.OutputLength = length;
                result.HasOutput = true;
            }
            return Start(result);
        }

        // ------------------------------------------------------------ sources

        internal static Result Buffer(GpuBufferStorage buffer, int size, int offset, Action<UnityEngine.Rendering.AsyncGPUReadbackRequest> callback)
        {
            if (buffer == null || buffer.Released) throw new ArgumentNullException("buffer");
            var result = new Result { Callback = callback };
            if (size <= 0 || offset < 0 || (long)offset + size > buffer.Size)
            {
                Debug.LogError($"AsyncGPUReadback - Out of bounds arguments - src offset:{offset} dst size:{size} src size:{buffer.Size}");
                result.HasError = true;
                return result;
            }
            var bytes = Native.ReadBuffer(buffer.Handle, offset, size);
            if (bytes == null) return Unavailable(result);
            result.Layers = new[] { bytes };
            result.Width = size;
            result.Height = result.Depth = 1;
            return result;
        }

        static Result Unavailable(Result result)
        {
            Debug.LogWarning("Unbound: reading back from the GPU needs a renderer (a headless run has none); the request fails.");
            result.HasError = true;
            return result;
        }

        /// <summary>The layers a texture has at a mip level: array slices, cube faces (times
        /// cubes) or a volume's depth at that level.</summary>
        static int Layers(Texture src, int mip)
        {
            switch (src)
            {
                case Texture3D t: return Math.Max(1, t.depth >> mip);
                case Texture2DArray t: return t.depth;
                case Cubemap _: return 6;
                case CubemapArray t: return t.cubemapCount * 6;
                case RenderTexture rt:
                    if (rt.dimension == UnityEngine.Rendering.TextureDimension.Tex3D) return Math.Max(1, rt.volumeDepth >> mip);
                    if (rt.dimension == UnityEngine.Rendering.TextureDimension.Cube) return 6;
                    if (rt.dimension == UnityEngine.Rendering.TextureDimension.Tex2DArray || rt.dimension == UnityEngine.Rendering.TextureDimension.CubeArray) return rt.volumeDepth;
                    return 1;
                default: return 1;
            }
        }

        internal static int LayerCount(Texture src, int mip) => src == null ? 1 : Layers(src, Math.Max(0, mip));

        /// <summary>Whether the declared format keeps 8 bits per channel: those read as the stored
        /// bytes; every other format reads as floats, which the renderer converts from whatever it
        /// stores the texture in (its storage may differ from the declared format). A packed format
        /// is never 8 bits per channel, even where its block has one byte per component
        /// (10-10-10-2: A2B10G10R10_UNormPack32).</summary>
        internal static bool EightBit(GraphicsFormat format) =>
            !GraphicsFormatUtility.IsHalfFormat(format) && !GraphicsFormatUtility.IsIEEE754Format(format)
            && !GraphicsFormatUtility.IsPackedFormat(format)
            && GraphicsFormatUtility.GetComponentCount(format) > 0
            && GraphicsFormatUtility.GetBlockSize(format) == GraphicsFormatUtility.GetComponentCount(format);

        /// <summary>Format conversion fills missing color channels with 1, independently of
        /// sampler defaults (6000.0.71f1: renderapi volumeReadback.*, textureformats rt.*).
        /// Unmeasured releases retain the earlier R16/RG32 conversion rule.</summary>
        static bool MissingChannelsReadAsOne(GraphicsFormat format) =>
            format is GraphicsFormat.R16_UNorm or GraphicsFormat.R16G16_UNorm
            || (Compat.RenderingMeasured && GraphicsFormatUtility.GetComponentCount(format) > 0
                && !GraphicsFormatUtility.IsDepthStencilFormat(format));

        /// <summary>A request in the texture's own format: that format's layout, or the packed
        /// words of a packed render texture where the device reads them
        /// (TargetReadbackRule.PackedWords; textures of those formats are unmeasured and keep
        /// failing, and so does a request naming the packed format explicitly).</summary>
        internal static ReadbackFormat Own(Texture src)
        {
            if (src == null) return null;
            var format = src.graphicsFormat;
            return ReadbackFormat.Of(format)
                ?? (src is RenderTexture && GraphicsDeviceProfile.Current.TargetReadback?.PackedWords?.Contains(format) == true
                    ? ReadbackFormat.Packed(format) : null);
        }

        internal static Result Texture(Texture src, int mipIndex, ReadbackFormat format, Action<UnityEngine.Rendering.AsyncGPUReadbackRequest> callback, bool reportUnsupported = false, bool validateSource = true)
        {
            if (src == null) throw new ArgumentNullException("src");
            // A dynamically scaled render texture's whole images (their scaled size).
            var rt = src as RenderTexture;
            int fullWidth = rt != null ? rt.ScaledWidth : src.width, fullHeight = rt != null ? rt.ScaledHeight : src.height;
            int w = Math.Max(1, fullWidth >> Math.Max(0, mipIndex)), h = Math.Max(1, fullHeight >> Math.Max(0, mipIndex));
            return Texture(src, mipIndex, 0, w, 0, h, 0, LayerCount(src, mipIndex), format, callback, reportUnsupported, validateSource);
        }

        static void UnsupportedReadPixels(GraphicsFormat format) =>
            Debug.LogError($"'{format}' doesn't support ReadPixels usage on this platform. Async GPU readback failed.");

        internal static Result Texture(Texture src, int mip, int x, int width, int y, int height, int z, int depth, ReadbackFormat format,
            Action<UnityEngine.Rendering.AsyncGPUReadbackRequest> callback, bool reportUnsupported = false, bool validateSource = true)
        {
            if (src == null) throw new ArgumentNullException("src");
            var result = new Result { Callback = callback };
            // Direct explicit-format requests validate the source before the request's own checks.
            // Command-buffer requests omit this entry-point diagnostic.
            // This diagnostic alone does not prevent converted texture data from being returned
            // (6000.0.71f1, tests/projects/nativegfx readbackValidation.*).
            bool reportedSource = Compat.RenderingMeasured && validateSource && reportUnsupported
                && GraphicsDeviceProfile.Current.ReadPixelsSupportIsMeasured(src.graphicsFormat)
                && !SystemInfo.IsFormatSupported(src.graphicsFormat, GraphicsFormatUsage.ReadPixels);
            if (reportedSource) UnsupportedReadPixels(src.graphicsFormat);
            if (Compat.RenderingMeasured && src.graphicsFormat == GraphicsFormat.None)
            {
                Debug.LogError("AsyncGPUReadback - None (0) graphics format is not supported as source for async read back");
                result.HasError = true;
                return result;
            }
            if (mip < 0 || mip >= Math.Max(1, src.mipmapCount))
            {
                Debug.LogError($"AsyncGPUReadback - Invalid mipIndex: {mip}");
                result.HasError = true;
                return result;
            }
            int lw = Math.Max(1, src.width >> mip), lh = Math.Max(1, src.height >> mip), ld = Layers(src, mip);
            // A dynamically scaled render texture's images are smaller than its size: what the
            // Editor reads back from them is unmeasured (ReadPixels reads zeros there); Unbound
            // reads the images, and refuses a region past them.
            if (src is RenderTexture scaled && scaled.DynamicallyScaled)
            {
                int sw = Math.Max(1, scaled.ScaledWidth >> mip), sh = Math.Max(1, scaled.ScaledHeight >> mip);
                Unbound.Runtime.Stubs.Ignored("AsyncGPUReadback of a dynamically scaled render texture", "reads its scaled images (unmeasured)");
                if (x + width > sw || y + height > sh)
                {
                    Debug.LogError($"Unbound: AsyncGPUReadback of the dynamically scaled render texture '{src.name}': the region ({x},{y}) {width}x{height} is past its scaled images ({sw}x{sh}); the request fails (unmeasured).");
                    result.HasError = true;
                    return result;
                }
            }
            if (x < 0 || y < 0 || z < 0 || width <= 0 || height <= 0 || depth <= 0 || x + width > lw || y + height > lh || z + depth > ld)
            {
                Debug.LogError($"AsyncGPUReadback - Out of bounds arguments - src offset:({x},{y},{z}) dst dim:({width},{height},{depth}) src dim:({lw},{lh},{ld})");
                result.HasError = true;
                return result;
            }
            if (format?.IsAlpha8 == true && !SystemInfo.IsFormatSupported(GraphicsFormatUtility.Texture2DAlpha8, GraphicsFormatUsage.ReadPixels))
            {
                // A valid texture still supplies dimensions when the requested readback format
                // is unsupported (tests/projects/gpubuffers --controls).
                result.Width = width;
                result.Height = height;
                result.Depth = depth;
                if (reportUnsupported && !reportedSource && (!Compat.RenderingMeasured
                    || !GraphicsDeviceProfile.Current.ReadPixelsSupportIsMeasured(GraphicsFormatUtility.Texture2DAlpha8)))
                    UnsupportedReadPixels(GraphicsFormatUtility.Texture2DAlpha8);
                result.HasError = true;
                return result;
            }
            // A render texture whose format the device cannot read pixels from (measured for
            // A2B10G10R10_UNormPack32 and the integer formats: tests/projects/textureformats). The
            // device's own rule reads one with GetPixels usage (metal.jsonl readback.*); integer
            // targets stay refused there (unmeasured).
            var rule = GraphicsDeviceProfile.Current.TargetReadback;
            var source = src.graphicsFormat;
            if (src is RenderTexture && rule != null && source != GraphicsFormat.None && !GraphicsFormatUtility.IsIntegerFormat(source)
                && !SystemInfo.IsFormatSupported(source, GraphicsFormatUsage.ReadPixels)
                && !SystemInfo.IsFormatSupported(source, GraphicsFormatUsage.GetPixels))
            {
                Debug.LogError(rule.RefusedSourceNames.TryGetValue(source, out var name)
                    ? $"AsyncGPUReadback - {name} ({(int)source}) graphics format is not supported as source for async read back"
                    : $"Unbound: AsyncGPUReadback of a {source} render texture is unmeasured on this device; the request fails.");
                result.HasError = true;
                return result;
            }
            if (src is RenderTexture && source != GraphicsFormat.None
                && (rule == null || GraphicsFormatUtility.IsIntegerFormat(source))
                && !SystemInfo.IsFormatSupported(source, GraphicsFormatUsage.ReadPixels))
            {
                if (!reportedSource) UnsupportedReadPixels(src.graphicsFormat);
                result.HasError = true;
                return result;
            }
            if (format == null || GraphicsFormatUtility.IsCompressedFormat(src.graphicsFormat))
            {
                Debug.LogError("Unbound: AsyncGPUReadback into this format is not supported; the request fails.");
                result.HasError = true;
                return result;
            }
            ulong handle = TextureHandles.Of(src);
            bool alpha = src switch
            {
                Texture2D t => t.format == TextureFormat.Alpha8,
                Texture3D t => t.format == TextureFormat.Alpha8,
                Texture2DArray t => t.format == TextureFormat.Alpha8,
                Cubemap t => t.format == TextureFormat.Alpha8,
                CubemapArray t => t.format == TextureFormat.Alpha8,
                _ => false,
            };
            bool alphaAsRed = alpha && Compat.DeviceTextureReadback && GraphicsDeviceProfile.Current.Alpha8Readback == Alpha8ReadbackMode.Red;
            bool floats = !EightBit(src.graphicsFormat) && !alphaAsRed;
            bool measuredLinux = Compat.DeviceTextureReadback && OperatingSystem.IsLinux()
                && GraphicsDeviceProfile.Current.Type is GraphicsDeviceType.OpenGLCore or GraphicsDeviceType.Vulkan;
            // The measured Linux devices expand a single R16 channel to RGB in byte layouts.
            bool replicateRed = measuredLinux && format.IsByte && src.graphicsFormat == GraphicsFormat.R16_UNorm;
            // Unsigned normalized 16-bit channels expand to f32 with a rounded reciprocal
            // (tests/projects/textureformats unorm16Readback).
            bool unorm16Reciprocal = measuredLinux && format.IsFloat32;
            int channels = alphaAsRed ? 1
                : MissingChannelsReadAsOne(src.graphicsFormat) ? (int)GraphicsFormatUtility.GetComponentCount(src.graphicsFormat) : 4;
            alpha &= !alphaAsRed;
            result.Layers = new byte[depth][];
            for (int layer = 0; layer < depth; layer++)
            {
                var texels = handle == 0 ? null : Native.ReadTexture(handle, mip, z + layer, x, y, width, height, floats,
                    unorm16Reciprocal: unorm16Reciprocal);
                if (texels == null) return Unavailable(result);
                if (floats && src is RenderTexture && !format.IsPacked) RenderTexture.QuantizeReadTexels(src.graphicsFormat, texels);
                result.Layers[layer] = format.Encode(texels, width * height, floats, alpha, channels,
                    Compat.DeviceTextureReadback && GraphicsDeviceProfile.Current.FramebufferReadback
                        && src.graphicsFormat == GraphicsFormat.R16G16B16_SNorm,
                    // Our Alpha8 upload keeps its value in RGBA's alpha component.
                    redSourceChannel: alphaAsRed ? 3 : 0, replicateRed: replicateRed);
            }
            result.Width = width;
            result.Height = height;
            result.Depth = depth;
            return result;
        }
    }

    /// <summary>The layout a texture readback is written in: channels in order, 8-bit, half or
    /// float, from the source's RGBA (bytes of 8-bit textures as they are, as floats b * (1 / 255) in
    /// single precision; floats round to bytes).</summary>
    internal sealed class ReadbackFormat
    {
        /// <summary>Source channel (0 r, 1 g, 2 b, 3 a) of each output channel.</summary>
        readonly int[] m_Channels;
        readonly int[] m_RedChannels;
        /// <summary>1 byte, 2 half (or a normalized 16-bit value), 4 float per channel.</summary>
        readonly int m_Size;
        /// <summary>Normalized 16-bit channels (with <see cref="m_Size"/> 2).</summary>
        readonly int m_Normalized16;

        /// <summary>The packed word layout, None for per-channel layouts.</summary>
        readonly PackedLayout m_Packed;

        internal bool IsAlpha8 => ReferenceEquals(this, A8);

        ReadbackFormat(int size, params int[] channels)
        {
            m_Size = size;
            m_Channels = channels;
            m_RedChannels = IsByte ? Array.ConvertAll(channels, c => c is 1 or 2 ? 0 : c) : channels;
        }
        ReadbackFormat(PackedLayout packed) : this(packed is PackedLayout.R4G4B4A4 or PackedLayout.A4R4G4B4 or PackedLayout.R5G6B5 ? 2 : 4, 0, 1, 2, 3) { m_Packed = packed; }

        /// <summary>A packed word per texel, little-endian, from the texel's RGBA (fields named
        /// from the word's highest bits down). Measured with the macOS Editor's Metal device
        /// (tests/projects/textureformats metal.jsonl readback.*.async, a target cleared to
        /// 0.25, 0.5, 0.75, 0.6): R4G4B4A4 reads R4G4B4A4 (0x48b9), B4G4R4A4 reads A4R4G4B4
        /// (0x948b), B5G6R5 reads R5G6B5 (0x4417), A2B10G10R10 reads A2B10G10R10 (0xaff80100),
        /// B10G11R11 the 10- and 11-bit unsigned floats B, G, R (0x741c0340), E5B9G9R9 the shared
        /// exponent and B, G, R (0x7e020080), R10G10B10_XR two set bits over the extended-range
        /// codes R, G, B (0xe009fefe; RenderTexture.ExtendedRangeCode).</summary>
        internal enum PackedLayout { None, R4G4B4A4, A4R4G4B4, R5G6B5, A2B10G10R10, B10G11R11, E5B9G9R9, X2R10G10B10XR }

        static readonly ReadbackFormat PackedR4G4B4A4 = new ReadbackFormat(PackedLayout.R4G4B4A4),
            PackedA4R4G4B4 = new ReadbackFormat(PackedLayout.A4R4G4B4), PackedR5G6B5 = new ReadbackFormat(PackedLayout.R5G6B5),
            PackedA2B10G10R10 = new ReadbackFormat(PackedLayout.A2B10G10R10), PackedB10G11R11 = new ReadbackFormat(PackedLayout.B10G11R11),
            PackedE5B9G9R9 = new ReadbackFormat(PackedLayout.E5B9G9R9), PackedX2R10G10B10XR = new ReadbackFormat(PackedLayout.X2R10G10B10XR);

        /// <summary>Whether this is a packed target's own word layout (read from the stored values,
        /// not from what reading the target into another format returns).</summary>
        internal bool IsPacked => m_Packed != PackedLayout.None;
        internal bool IsByte => m_Size == sizeof(byte);
        internal bool IsFloat32 => !IsPacked && m_Size == sizeof(float);

        /// <summary>The packed words a request in a packed target's own format reads.</summary>
        internal static ReadbackFormat Packed(GraphicsFormat format) => format switch
        {
            GraphicsFormat.R4G4B4A4_UNormPack16 => PackedR4G4B4A4,
            GraphicsFormat.B4G4R4A4_UNormPack16 => PackedA4R4G4B4,
            GraphicsFormat.B5G6R5_UNormPack16 => PackedR5G6B5,
            GraphicsFormat.A2B10G10R10_UNormPack32 => PackedA2B10G10R10,
            GraphicsFormat.B10G11R11_UFloatPack32 => PackedB10G11R11,
            GraphicsFormat.E5B9G9R9_UFloatPack32 => PackedE5B9G9R9,
            GraphicsFormat.R10G10B10_XRUNormPack32 => PackedX2R10G10B10XR,
            _ => null,
        };

        static uint Unorm(float value, int bits) => (uint)Math.Round(Math.Clamp(value, 0f, 1f) * ((1 << bits) - 1), MidpointRounding.AwayFromZero);

        /// <summary>An unsigned float of 5 exponent bits (bias 15) and <paramref name="mantissaBits"/>,
        /// rounded to the nearest (the 10- and 11-bit floats of B10G11R11); its largest finite
        /// value for larger finite ones, infinity and NaN as the format writes them, 0 for
        /// negative ones.</summary>
        internal static uint UnsignedFloat(float value, int mantissaBits)
        {
            // NaN's mantissa bits are a choice (unmeasured): any nonzero mantissa is a NaN.
            if (float.IsNaN(value)) return (31u << mantissaBits) | ((1u << mantissaBits) - 1);
            if (float.IsPositiveInfinity(value)) return 31u << mantissaBits;
            if (!(value > 0f)) return 0;
            uint largest = (30u << mantissaBits) | ((1u << mantissaBits) - 1);
            uint bits = BitConverter.SingleToUInt32Bits(value);
            int exponent = (int)((bits >> 23) & 0xff) - 127 + 15;
            uint mantissa = (bits & 0x7fffff) | 0x800000;
            // The mantissa with its leading one, shifted to the field's precision (further for
            // values below the smallest normal one), rounded to the nearest.
            int drop = 23 - mantissaBits + Math.Max(0, 1 - exponent);
            if (drop > 24) return 0;
            uint rounded = (mantissa + (1u << (drop - 1))) >> drop;
            uint word = exponent <= 0 ? rounded : ((uint)exponent << mantissaBits) + (rounded - (1u << mantissaBits));
            return Math.Min(word, largest);
        }

        /// <summary>E5B9G9R9: three 9-bit mantissas sharing a 5-bit exponent (bias 15), R in the
        /// lowest bits, the exponent the largest component's.</summary>
        internal static uint SharedExponent(float r, float g, float b)
        {
            const int Mantissa = 9, Bias = 15, MaxExponent = 31;
            float largest = (float)((1 << Mantissa) - 1) / (1 << Mantissa) * (1 << (MaxExponent - Bias));
            r = float.IsNaN(r) ? 0f : Math.Clamp(r, 0f, largest);
            g = float.IsNaN(g) ? 0f : Math.Clamp(g, 0f, largest);
            b = float.IsNaN(b) ? 0f : Math.Clamp(b, 0f, largest);
            float max = Math.Max(r, Math.Max(g, b));
            int exponent = max > 0f ? Math.Max(-Bias - 1, (int)Math.Floor(Math.Log2(max))) + 1 + Bias : 0;
            double scale = Math.Pow(2, exponent - Bias - Mantissa);
            if (Math.Round(max / scale, MidpointRounding.AwayFromZero) >= 1 << Mantissa) { exponent++; scale *= 2; }
            uint R = (uint)Math.Round(r / scale, MidpointRounding.AwayFromZero), G = (uint)Math.Round(g / scale, MidpointRounding.AwayFromZero),
                B = (uint)Math.Round(b / scale, MidpointRounding.AwayFromZero);
            return R | (G << 9) | (B << 18) | ((uint)exponent << 27);
        }

        uint PackWord(float r, float g, float b, float a) => m_Packed switch
        {
            PackedLayout.R4G4B4A4 => Unorm(r, 4) << 12 | Unorm(g, 4) << 8 | Unorm(b, 4) << 4 | Unorm(a, 4),
            PackedLayout.A4R4G4B4 => Unorm(a, 4) << 12 | Unorm(r, 4) << 8 | Unorm(g, 4) << 4 | Unorm(b, 4),
            PackedLayout.R5G6B5 => Unorm(r, 5) << 11 | Unorm(g, 6) << 5 | Unorm(b, 5),
            PackedLayout.A2B10G10R10 => Unorm(a, 2) << 30 | Unorm(b, 10) << 20 | Unorm(g, 10) << 10 | Unorm(r, 10),
            PackedLayout.B10G11R11 => UnsignedFloat(b, 5) << 22 | UnsignedFloat(g, 6) << 11 | UnsignedFloat(r, 6),
            PackedLayout.E5B9G9R9 => SharedExponent(r, g, b),
            PackedLayout.X2R10G10B10XR => 3u << 30 | RenderTexture.ExtendedRangeCode(r) << 20
                | RenderTexture.ExtendedRangeCode(g) << 10 | RenderTexture.ExtendedRangeCode(b),
            _ => throw new InvalidOperationException(),
        };
        ReadbackFormat(bool signed, params int[] channels) : this(2, channels) { m_Normalized16 = signed ? -1 : 1; }

        static readonly ReadbackFormat Rgba8 = new ReadbackFormat(1, 0, 1, 2, 3), Bgra8 = new ReadbackFormat(1, 2, 1, 0, 3),
            Argb8 = new ReadbackFormat(1, 3, 0, 1, 2), R8 = new ReadbackFormat(1, 0), Rg8 = new ReadbackFormat(1, 0, 1), Rgb8 = new ReadbackFormat(1, 0, 1, 2),
            A8 = new ReadbackFormat(1, 3), RgbaHalf = new ReadbackFormat(2, 0, 1, 2, 3), RHalf = new ReadbackFormat(2, 0), RgHalf = new ReadbackFormat(2, 0, 1),
            RgbaFloat = new ReadbackFormat(4, 0, 1, 2, 3), RFloat = new ReadbackFormat(4, 0), RgFloat = new ReadbackFormat(4, 0, 1),
            R16 = new ReadbackFormat(false, 0), Rg16 = new ReadbackFormat(false, 0, 1), Rgba16 = new ReadbackFormat(false, 0, 1, 2, 3),
            RS16 = new ReadbackFormat(true, 0), RgS16 = new ReadbackFormat(true, 0, 1),
            RgbS16 = new ReadbackFormat(true, 0, 1, 2), RgbaS16 = new ReadbackFormat(true, 0, 1, 2, 3);

        internal static ReadbackFormat Of(TextureFormat format) => format switch
        {
            TextureFormat.RGBA32 => Rgba8,
            TextureFormat.BGRA32 => Bgra8,
            TextureFormat.ARGB32 => Argb8,
            TextureFormat.RGB24 => Rgb8,
            TextureFormat.R8 => R8,
            TextureFormat.RG16 => Rg8,
            TextureFormat.Alpha8 => A8,
            TextureFormat.RGBAHalf => RgbaHalf,
            TextureFormat.RHalf => RHalf,
            TextureFormat.RGHalf => RgHalf,
            TextureFormat.RGBAFloat => RgbaFloat,
            TextureFormat.RFloat => RFloat,
            TextureFormat.RGFloat => RgFloat,
            TextureFormat.R16 => R16,
            TextureFormat.RG32 => Rg16,
            TextureFormat.RGBA64 => Rgba16,
            _ => null,
        };

        internal static ReadbackFormat Of(GraphicsFormat format) => format switch
        {
            GraphicsFormatUtility.Texture2DAlpha8 => A8,
            GraphicsFormat.R8G8B8A8_UNorm or GraphicsFormat.R8G8B8A8_SRGB => Rgba8,
            GraphicsFormat.B8G8R8A8_UNorm or GraphicsFormat.B8G8R8A8_SRGB => Bgra8,
            GraphicsFormat.R8G8B8_UNorm or GraphicsFormat.R8G8B8_SRGB => Rgb8,
            GraphicsFormat.R8_UNorm or GraphicsFormat.R8_SRGB => R8,
            GraphicsFormat.R8G8_UNorm or GraphicsFormat.R8G8_SRGB => Rg8,
            GraphicsFormat.R16G16B16A16_SFloat => RgbaHalf,
            GraphicsFormat.R16_SFloat => RHalf,
            GraphicsFormat.R16G16_SFloat => RgHalf,
            GraphicsFormat.R32G32B32A32_SFloat => RgbaFloat,
            GraphicsFormat.R32_SFloat => RFloat,
            GraphicsFormat.R32G32_SFloat => RgFloat,
            GraphicsFormat.R16_SNorm => RS16,
            GraphicsFormat.R16G16_SNorm => RgS16,
            GraphicsFormat.R16G16B16_SNorm => RgbS16,
            GraphicsFormat.R16G16B16A16_SNorm => RgbaS16,
            GraphicsFormat.R16_UNorm => R16,
            GraphicsFormat.R16G16_UNorm => Rg16,
            GraphicsFormat.R16G16B16A16_UNorm => Rgba16,
            _ => null,
        };

        /// <summary><paramref name="texels"/> as the renderer reads them: 4 bytes per texel of an
        /// 8-bit RGBA texture (1 of a one-channel one), 4 floats when <paramref name="floats"/>, of
        /// which those from <paramref name="sourceChannels"/> on read as 1.</summary>
        internal byte[] Encode(byte[] texels, int count, bool floats, bool alpha = false,
            int sourceChannels = 4, bool clampNegative = false, int redSourceChannel = 0, bool replicateRed = false)
        {
            int inChannels = floats ? 4 : Math.Max(1, texels.Length / Math.Max(1, count));
            if (m_Packed != PackedLayout.None) return EncodePacked(texels, count, floats, inChannels, sourceChannels);
            var channels = replicateRed ? m_RedChannels : m_Channels;
            var output = new byte[count * channels.Length * m_Size];
            int o = 0;
            for (int i = 0; i < count; i++)
            {
                foreach (int c in channels)
                {
                    int storedChannel = inChannels == 4 && c == 0 ? redSourceChannel : c;
                    float f;
                    byte b;
                    if (floats)
                    {
                        f = c < sourceChannels ? BitConverter.ToSingle(texels, (i * 4 + storedChannel) * 4) : 1f;
                        if (clampNegative) f = Math.Max(0f, f);
                        b = (byte)Math.Round(Math.Clamp(f, 0f, 1f) * 255f, MidpointRounding.AwayFromZero);
                    }
                    else
                    {
                        // Explicit conversion fills missing color channels; Alpha8 keeps alpha.
                        b = !alpha && c >= sourceChannels ? (byte)255
                            : inChannels == 4 ? texels[i * 4 + storedChannel]
                            : alpha ? (c == 3 ? texels[i * inChannels] : (byte)0)
                            : c < inChannels ? texels[i * inChannels + c] : c == 3 ? (byte)255 : (byte)0;
                        f = b * (1f / 255f);
                    }
                    switch (m_Size)
                    {
                        case 1: output[o] = b; break;
                        case 2 when m_Normalized16 > 0:
                            BitConverter.TryWriteBytes(new Span<byte>(output, o, 2), (ushort)Math.Round(Math.Clamp(f, 0f, 1f) * 65535.0));
                            break;
                        case 2 when m_Normalized16 < 0:
                            BitConverter.TryWriteBytes(new Span<byte>(output, o, 2), (short)Math.Round(Math.Clamp(f, -1f, 1f) * 32767.0));
                            break;
                        case 2: BitConverter.TryWriteBytes(new Span<byte>(output, o, 2), (Half)f); break;
                        default: BitConverter.TryWriteBytes(new Span<byte>(output, o, 4), f); break;
                    }
                    o += m_Size;
                }
            }
            return output;
        }

        /// <summary>One packed word per texel from the stored texels (floats, or 8-bit channels as
        /// b / 255); channels the source lacks read as 1.</summary>
        byte[] EncodePacked(byte[] texels, int count, bool floats, int inChannels, int sourceChannels)
        {
            var output = new byte[count * m_Size];
            Span<float> c = stackalloc float[4];
            for (int i = 0; i < count; i++)
            {
                for (int k = 0; k < 4; k++)
                    c[k] = k >= sourceChannels || (!floats && k >= inChannels) ? 1f
                        : floats ? BitConverter.ToSingle(texels, (i * 4 + k) * 4) : texels[i * inChannels + k] * (1f / 255f);
                uint word = PackWord(c[0], c[1], c[2], c[3]);
                if (m_Size == 2) BitConverter.TryWriteBytes(new Span<byte>(output, i * 2, 2), (ushort)word);
                else BitConverter.TryWriteBytes(new Span<byte>(output, i * 4, 4), word);
            }
            return output;
        }
    }
}

namespace UnityEngine.Rendering
{
    public partial struct AsyncGPUReadbackRequest
    {
        internal IntPtr m_Ptr;
        internal int m_Version;

        /// <summary>The request's state; null once its data was released (or for a default request).</summary>
        AsyncReadbacks.Result Result => m_Ptr != IntPtr.Zero ? AsyncReadbacks.Find(m_Ptr) : null;

        [Unbound.EditorFaithful("the Editor completes a request in the readback manager's update, not here")]
        public void Update() { }
        public void WaitForCompletion()
        {
            if (m_Ptr != IntPtr.Zero) AsyncReadbacks.Wait(m_Ptr);
        }

        public bool done => m_Ptr != IntPtr.Zero && (Result?.Done ?? true);
        public bool hasError => Result is not { } r || r.HasError;
        public int layerCount => Available ? Result.Layers.Length : 0;
        public int layerDataSize => Available && Result.Layers.Length > 0 ? Result.Layers[0].Length : 0;
        public int width => Result?.Width ?? 0;
        public int height => Result?.Height ?? 0;
        public int depth => Result?.Depth ?? 0;
        bool m_Value_forcePlayerLoopUpdate;
        public bool forcePlayerLoopUpdate { get => m_Value_forcePlayerLoopUpdate; set => m_Value_forcePlayerLoopUpdate = value; }

        bool Available => Result is { } r && !r.HasError;

        /// <summary>A layer's data as <typeparamref name="T"/>, for the frame the request completed in.</summary>
        public unsafe NativeArray<T> GetData<T>(int layer = 0) where T : struct
        {
            var result = Result;
            if (result == null || !result.Done || result.HasError) throw new InvalidOperationException("Cannot access the data as it is not available");
            if (layer < 0 || layer >= result.Layers.Length) throw new ArgumentException($"Layer index is out of range {layer} / {result.Layers.Length}");
            var bytes = result.Layers[layer];
            int size = UnsafeUtility.SizeOf<T>();
            if (bytes.Length % size != 0) throw new ArgumentException($"Type {typeof(T)} doesn't divide the layer's {bytes.Length} bytes");
            var array = new NativeArray<T>(bytes.Length / size, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            fixed (byte* p = bytes) UnsafeUtility.MemCpy(NativeArrayUnsafeUtility.GetUnsafePtr(array), p, bytes.Length);
            return array;
        }
    }

    public static partial class AsyncGPUReadback
    {
        public static void WaitAllRequests() => AsyncReadbacks.WaitAll();

        static AsyncReadbacks.Result Buffer(GpuBufferStorage src, int size, int offset, Action<AsyncGPUReadbackRequest> callback) =>
            AsyncReadbacks.Buffer(src, size, offset, callback);

        public static AsyncGPUReadbackRequest Request(ComputeBuffer src, Action<AsyncGPUReadbackRequest> callback = null) =>
            AsyncReadbacks.Start(Buffer(src?.Storage, (int)(src?.Storage?.Size ?? 0), 0, callback));
        public static AsyncGPUReadbackRequest Request(ComputeBuffer src, int size, int offset, Action<AsyncGPUReadbackRequest> callback = null) =>
            AsyncReadbacks.Start(Buffer(src?.Storage, size, offset, callback));
        public static AsyncGPUReadbackRequest Request(GraphicsBuffer src, Action<AsyncGPUReadbackRequest> callback = null) =>
            AsyncReadbacks.Start(Buffer(src?.Storage, (int)(src?.Storage?.Size ?? 0), 0, callback));
        public static AsyncGPUReadbackRequest Request(GraphicsBuffer src, int size, int offset, Action<AsyncGPUReadbackRequest> callback = null) =>
            AsyncReadbacks.Start(Buffer(src?.Storage, size, offset, callback));

        public static AsyncGPUReadbackRequest Request(Texture src, int mipIndex = 0, Action<AsyncGPUReadbackRequest> callback = null) =>
            AsyncReadbacks.Start(AsyncReadbacks.Texture(src, mipIndex, AsyncReadbacks.Own(src), callback));
        public static AsyncGPUReadbackRequest Request(Texture src, int mipIndex, TextureFormat dstFormat, Action<AsyncGPUReadbackRequest> callback = null) =>
            AsyncReadbacks.Start(AsyncReadbacks.Texture(src, mipIndex, ReadbackFormat.Of(dstFormat), callback, reportUnsupported: true));
        public static AsyncGPUReadbackRequest Request(Texture src, int mipIndex, GraphicsFormat dstFormat, Action<AsyncGPUReadbackRequest> callback = null) =>
            AsyncReadbacks.Start(AsyncReadbacks.Texture(src, mipIndex, ReadbackFormat.Of(dstFormat), callback, reportUnsupported: true));
        public static AsyncGPUReadbackRequest Request(Texture src, int mipIndex, int x, int width, int y, int height, int z, int depth, Action<AsyncGPUReadbackRequest> callback = null) =>
            AsyncReadbacks.Start(AsyncReadbacks.Texture(src, mipIndex, x, width, y, height, z, depth, AsyncReadbacks.Own(src), callback));
        public static AsyncGPUReadbackRequest Request(Texture src, int mipIndex, int x, int width, int y, int height, int z, int depth, TextureFormat dstFormat, Action<AsyncGPUReadbackRequest> callback = null) =>
            AsyncReadbacks.Start(AsyncReadbacks.Texture(src, mipIndex, x, width, y, height, z, depth, ReadbackFormat.Of(dstFormat), callback, reportUnsupported: true));
        public static AsyncGPUReadbackRequest Request(Texture src, int mipIndex, int x, int width, int y, int height, int z, int depth, GraphicsFormat dstFormat, Action<AsyncGPUReadbackRequest> callback = null) =>
            AsyncReadbacks.Start(AsyncReadbacks.Texture(src, mipIndex, x, width, y, height, z, depth, ReadbackFormat.Of(dstFormat), callback, reportUnsupported: true));

        public static AsyncGPUReadbackRequest RequestIntoNativeSlice<T>(ref NativeSlice<T> output, ComputeBuffer src, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            AsyncReadbacks.Into(ref output, Buffer(src?.Storage, (int)(src?.Storage?.Size ?? 0), 0, callback));
        public static AsyncGPUReadbackRequest RequestIntoNativeSlice<T>(ref NativeSlice<T> output, ComputeBuffer src, int size, int offset, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            AsyncReadbacks.Into(ref output, Buffer(src?.Storage, size, offset, callback));
        public static AsyncGPUReadbackRequest RequestIntoNativeSlice<T>(ref NativeSlice<T> output, GraphicsBuffer src, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            AsyncReadbacks.Into(ref output, Buffer(src?.Storage, (int)(src?.Storage?.Size ?? 0), 0, callback));
        public static AsyncGPUReadbackRequest RequestIntoNativeSlice<T>(ref NativeSlice<T> output, GraphicsBuffer src, int size, int offset, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            AsyncReadbacks.Into(ref output, Buffer(src?.Storage, size, offset, callback));
        public static AsyncGPUReadbackRequest RequestIntoNativeSlice<T>(ref NativeSlice<T> output, Texture src, int mipIndex = 0, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            AsyncReadbacks.Into(ref output, AsyncReadbacks.Texture(src, mipIndex, AsyncReadbacks.Own(src), callback));
        public static AsyncGPUReadbackRequest RequestIntoNativeSlice<T>(ref NativeSlice<T> output, Texture src, int mipIndex, TextureFormat dstFormat, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            AsyncReadbacks.Into(ref output, AsyncReadbacks.Texture(src, mipIndex, ReadbackFormat.Of(dstFormat), callback, reportUnsupported: true));
        public static AsyncGPUReadbackRequest RequestIntoNativeSlice<T>(ref NativeSlice<T> output, Texture src, int mipIndex, GraphicsFormat dstFormat, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            AsyncReadbacks.Into(ref output, AsyncReadbacks.Texture(src, mipIndex, ReadbackFormat.Of(dstFormat), callback, reportUnsupported: true));
        public static AsyncGPUReadbackRequest RequestIntoNativeSlice<T>(ref NativeSlice<T> output, Texture src, int mipIndex, int x, int width, int y, int height, int z, int depth, TextureFormat dstFormat, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            AsyncReadbacks.Into(ref output, AsyncReadbacks.Texture(src, mipIndex, x, width, y, height, z, depth, ReadbackFormat.Of(dstFormat), callback, reportUnsupported: true));
        public static AsyncGPUReadbackRequest RequestIntoNativeSlice<T>(ref NativeSlice<T> output, Texture src, int mipIndex, int x, int width, int y, int height, int z, int depth, GraphicsFormat dstFormat, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            AsyncReadbacks.Into(ref output, AsyncReadbacks.Texture(src, mipIndex, x, width, y, height, z, depth, ReadbackFormat.Of(dstFormat), callback, reportUnsupported: true));

        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, ComputeBuffer src, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            AsyncReadbacks.Into(ref output, Buffer(src?.Storage, (int)(src?.Storage?.Size ?? 0), 0, callback));
        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, ComputeBuffer src, int size, int offset, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            AsyncReadbacks.Into(ref output, Buffer(src?.Storage, size, offset, callback));
        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, GraphicsBuffer src, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            AsyncReadbacks.Into(ref output, Buffer(src?.Storage, (int)(src?.Storage?.Size ?? 0), 0, callback));
        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, GraphicsBuffer src, int size, int offset, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            AsyncReadbacks.Into(ref output, Buffer(src?.Storage, size, offset, callback));
        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, Texture src, int mipIndex = 0, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            AsyncReadbacks.Into(ref output, AsyncReadbacks.Texture(src, mipIndex, AsyncReadbacks.Own(src), callback));
        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, Texture src, int mipIndex, TextureFormat dstFormat, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            AsyncReadbacks.Into(ref output, AsyncReadbacks.Texture(src, mipIndex, ReadbackFormat.Of(dstFormat), callback, reportUnsupported: true));
        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, Texture src, int mipIndex, GraphicsFormat dstFormat, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            AsyncReadbacks.Into(ref output, AsyncReadbacks.Texture(src, mipIndex, ReadbackFormat.Of(dstFormat), callback, reportUnsupported: true));
        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, Texture src, int mipIndex, int x, int width, int y, int height, int z, int depth, TextureFormat dstFormat, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            AsyncReadbacks.Into(ref output, AsyncReadbacks.Texture(src, mipIndex, x, width, y, height, z, depth, ReadbackFormat.Of(dstFormat), callback, reportUnsupported: true));
        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, Texture src, int mipIndex, int x, int width, int y, int height, int z, int depth, GraphicsFormat dstFormat, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            AsyncReadbacks.Into(ref output, AsyncReadbacks.Texture(src, mipIndex, x, width, y, height, z, depth, ReadbackFormat.Of(dstFormat), callback, reportUnsupported: true));
    }
}
