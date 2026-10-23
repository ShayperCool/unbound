using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Unbound.Runtime
{
    /// <summary>A byte buffer owned by the native side; release it with <see cref="Native.Free"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct NativeBuffer
    {
        internal byte* Data;
        internal long Length;
    }

    /// <summary>
    /// The functions the native side (Rust) gives the engine at start. The layout is shared with
    /// `unbound-runtime/src/native_api.rs`; both sides check <see cref="Version"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct NativeApi
    {
        internal const uint CurrentVersion = 1;

        internal uint Version;
        /// <summary>(level, utf8, length): writes a console line.</summary>
        internal delegate* unmanaged<int, byte*, int, void> Log;
        /// <summary>(method, length, request, length, out response): a control call with JSON
        /// in and out; returns 0 on success, else the response holds an error message.</summary>
        internal delegate* unmanaged<byte*, int, byte*, int, NativeBuffer*, int> Call;
        /// <summary>Releases a buffer the native side returned.</summary>
        internal delegate* unmanaged<NativeBuffer, void> FreeBuffer;
        /// <summary>(asset path or guid, length, out documents): the YAML documents of a scene,
        /// prefab or asset file with prefab instances expanded, in the binary node encoding.</summary>
        internal delegate* unmanaged<byte*, int, NativeBuffer*, int> LoadDocuments;
        /// <summary>(render frame): hands the frame's draw lists to the renderer.</summary>
        internal delegate* unmanaged<void*, void> SubmitFrame;
    }

    internal static unsafe class Native
    {
        static NativeApi s_Api;
        static bool s_Ready;

        internal static void Initialize(NativeApi* api)
        {
            if (api->Version != NativeApi.CurrentVersion)
                throw new InvalidOperationException($"native API version {api->Version}, managed expects {NativeApi.CurrentVersion}");
            s_Api = *api;
            s_Ready = true;
        }

        internal static bool IsReady => s_Ready;

        internal static void Log(int level, string text)
        {
            if (!s_Ready || s_Api.Log == null)
            {
                Console.Error.WriteLine(text);
                return;
            }
            var bytes = Encoding.UTF8.GetBytes(text);
            fixed (byte* p = bytes) s_Api.Log(level, p, bytes.Length);
        }

        /// <summary>A control call into the native side. Throws with the native error message.</summary>
        internal static string Call(string method, string request)
        {
            if (!s_Ready) throw new InvalidOperationException("the native side is not connected");
            var m = Encoding.UTF8.GetBytes(method);
            var r = Encoding.UTF8.GetBytes(request ?? "");
            NativeBuffer response;
            int status;
            fixed (byte* mp = m)
            fixed (byte* rp = r)
                status = s_Api.Call(mp, m.Length, rp, r.Length, &response);
            try
            {
                var text = response.Length == 0 ? "" : Encoding.UTF8.GetString(response.Data, (int)response.Length);
                if (status != 0) throw new InvalidOperationException($"native `{method}` failed: {text}");
                return text;
            }
            finally
            {
                Free(response);
            }
        }

        internal static void Free(NativeBuffer buffer)
        {
            if (buffer.Data != null && s_Api.FreeBuffer != null) s_Api.FreeBuffer(buffer);
        }

        /// <summary>The resolved documents of a file (`Assets/...` path or 32-hex guid).</summary>
        internal static System.Collections.Generic.List<YDocument> LoadDocuments(string pathOrGuid)
        {
            if (!s_Ready) throw new InvalidOperationException("the native side is not connected");
            var key = Encoding.UTF8.GetBytes(pathOrGuid);
            NativeBuffer buffer;
            int status;
            fixed (byte* k = key) status = s_Api.LoadDocuments(k, key.Length, &buffer);
            try
            {
                if (status != 0)
                {
                    var error = buffer.Length == 0 ? "unknown error" : Encoding.UTF8.GetString(buffer.Data, (int)buffer.Length);
                    throw new InvalidOperationException($"loading {pathOrGuid}: {error}");
                }
                return YBinary.DecodeDocuments(buffer.Data, buffer.Length);
            }
            finally
            {
                Free(buffer);
            }
        }

        internal static void SubmitFrame(void* frame)
        {
            if (s_Ready && s_Api.SubmitFrame != null) s_Api.SubmitFrame(frame);
        }
    }
}
