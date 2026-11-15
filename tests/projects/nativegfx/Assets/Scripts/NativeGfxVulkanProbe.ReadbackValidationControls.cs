using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

public partial class NativeGfxVulkanProbe
{
    void ReadbackValidationControls()
    {
        var previousLogs = logs.ToArray(); logs.Clear();
        foreach (var format in new[] { TextureFormat.RGBA32, TextureFormat.BGRA32 })
        {
            var source = ReadbackValidationSource(format);
            string key = "readbackValidation." + format;
            Value(key + ".readPixelsSupported", B(SystemInfo.IsFormatSupported(source.graphicsFormat, GraphicsFormatUsage.ReadPixels)));
            try
            {
                for (int mode = 0; mode < 10; mode++)
                {
                    logs.Clear();
                    var output = new NativeArray<byte>(24, Allocator.Persistent);
                    try
                    {
                        AsyncGPUReadbackRequest request;
                        switch (mode)
                        {
                            case 0: request = AsyncGPUReadback.Request(source, 0); break;
                            case 1: request = AsyncGPUReadback.Request(source, 0, TextureFormat.RGBA32); break;
                            case 2: request = AsyncGPUReadback.Request(source, 0, GraphicsFormat.R8G8B8A8_UNorm); break;
                            case 3: request = AsyncGPUReadback.Request(source, 0, 1, 2, 0, 2, 0, 1, GraphicsFormat.R8G8B8A8_UNorm); break;
                            case 4: request = AsyncGPUReadback.RequestIntoNativeArray(ref output, source, 0, GraphicsFormat.R8G8B8A8_UNorm); break;
                            case 5: request = AsyncGPUReadback.RequestIntoNativeArray(ref output, source, 0, 1, 2, 0, 2, 0, 1, GraphicsFormat.R8G8B8A8_UNorm); break;
                            case 6:
                                var slice = new NativeSlice<byte>(output);
                                request = AsyncGPUReadback.RequestIntoNativeSlice(ref slice, source, 0, GraphicsFormat.R8G8B8A8_UNorm); break;
                            case 7: request = AsyncGPUReadback.Request(source, 0, 1, 2, 0, 2, 0, 1); break;
                            case 8: request = AsyncGPUReadback.Request(source, 2, GraphicsFormat.R8G8B8A8_UNorm); break;
                            default: request = AsyncGPUReadback.Request(source, 2); break;
                        }
                        request.WaitForCompletion();
                        string at = key + "." + mode;
                        Value(at + ".state", Q(request.hasError + ":" + request.width + ":" + request.height + ":" + request.depth));
                        if (!request.hasError)
                        {
                            var data = mode >= 4 && mode <= 6 ? output : request.GetData<byte>();
                            Value(at + ".data", Q(data[0].ToString("x2") + data[1].ToString("x2") + data[2].ToString("x2") + data[3].ToString("x2")));
                        }
                        Value(at + ".logs", L(logs));
                    }
                    finally { output.Dispose(); }
                }
            }
            finally { DestroyImmediate(source); }
        }
        ReadbackDepthValidationControls();
        ReadbackCommandControls();
        ReadbackAlphaControls();
        logs.Clear(); logs.AddRange(previousLogs);
    }

    void ReadbackDepthValidationControls()
    {
        var source = new RenderTexture(3, 2, 24, RenderTextureFormat.Depth);
        try
        {
            Value("readbackValidation.depthOnly.created", B(source.Create()));
            Value("readbackValidation.depthOnly.format", Q(source.graphicsFormat.ToString()));
            Value("readbackValidation.depthOnly.readPixelsSupported", B(SystemInfo.IsFormatSupported(source.graphicsFormat, GraphicsFormatUsage.ReadPixels)));
            for (int mode = 0; mode < 2; mode++)
            {
                logs.Clear();
                var request = mode == 0
                    ? AsyncGPUReadback.Request(source, 2, GraphicsFormat.R8G8B8A8_UNorm)
                    : AsyncGPUReadback.Request(source, 2);
                request.WaitForCompletion();
                string key = "readbackValidation.depthOnly." + mode;
                Value(key + ".state", Q(request.hasError + ":" + request.width + ":" + request.height + ":" + request.depth));
                Value(key + ".logs", L(logs));
            }
        }
        finally { DestroyImmediate(source); }
    }

    static Texture2D ReadbackValidationSource(TextureFormat format, byte alpha = 255)
    {
        var source = new Texture2D(3, 2, format, false, true);
        var bytes = new byte[format == TextureFormat.Alpha8 ? 6 : 24];
        for (int i = 0; i < bytes.Length; i += format == TextureFormat.Alpha8 ? 1 : 4)
        {
            if (format == TextureFormat.Alpha8) { bytes[i] = (byte)(i * 41); continue; }
            bytes[i] = format == TextureFormat.BGRA32 ? (byte)192 : (byte)64;
            bytes[i + 1] = 128;
            bytes[i + 2] = format == TextureFormat.BGRA32 ? (byte)64 : (byte)192;
            bytes[i + 3] = alpha;
        }
        source.LoadRawTextureData(bytes); source.Apply(false, false);
        return source;
    }

    void ReadbackCommandControls()
    {
        foreach (var format in new[] { TextureFormat.RGBA32, TextureFormat.BGRA32, TextureFormat.Alpha8 })
        {
            var source = ReadbackValidationSource(format);
            try
            {
                for (int mode = 0; mode < 7; mode++)
                {
                    logs.Clear();
                    string key = "readbackValidation.command." + format + "." + mode;
                    var output = new NativeArray<byte>(24, Allocator.Persistent);
                    var cmd = new CommandBuffer();
                    string state = "no callback", data = "";
                    Action<AsyncGPUReadbackRequest> callback = request =>
                    {
                        state = request.hasError + ":" + request.width + ":" + request.height + ":" + request.depth;
                        if (!request.hasError)
                        {
                            var values = mode == 4 || mode == 5 ? output : request.GetData<byte>();
                            data = values[0].ToString("x2") + values[1].ToString("x2") + values[2].ToString("x2") + values[3].ToString("x2");
                        }
                    };
                    try
                    {
                        switch (mode)
                        {
                            case 0: cmd.RequestAsyncReadback(source, 0, callback); break;
                            case 1: cmd.RequestAsyncReadback(source, 0, TextureFormat.RGBA32, callback); break;
                            case 2: cmd.RequestAsyncReadback(source, 0, GraphicsFormat.R8G8B8A8_UNorm, callback); break;
                            case 3: cmd.RequestAsyncReadback(source, 0, 1, 2, 0, 2, 0, 1, GraphicsFormat.R8G8B8A8_UNorm, callback); break;
                            case 4: cmd.RequestAsyncReadbackIntoNativeArray(ref output, source, 0, GraphicsFormat.R8G8B8A8_UNorm, callback); break;
                            case 5:
                                var slice = new NativeSlice<byte>(output);
                                cmd.RequestAsyncReadbackIntoNativeSlice(ref slice, source, 0, GraphicsFormat.R8G8B8A8_UNorm, callback); break;
                            default: cmd.RequestAsyncReadback(source, 2, GraphicsFormat.R8G8B8A8_UNorm, callback); break;
                        }
                        Value(key + ".recordLogs", L(logs)); logs.Clear();
                        cmd.WaitAllAsyncReadbackRequests();
                        Graphics.ExecuteCommandBuffer(cmd);
                        Value(key + ".state", Q(state));
                        if (data.Length > 0) Value(key + ".data", Q(data));
                        Value(key + ".executeLogs", L(logs));
                    }
                    finally { cmd.Release(); output.Dispose(); }
                }
            }
            finally { DestroyImmediate(source); }
        }
    }

    void ReadbackAlphaControls()
    {
        foreach (var format in new[] { TextureFormat.RGBA32, TextureFormat.Alpha8 })
        {
            var source = ReadbackValidationSource(format, 32);
            string at = "readbackValidation.alpha." + format;
            try
            {
                Value(at + ".format", Q(source.graphicsFormat.ToString()));
                Value(at + ".readPixelsSupported", B(SystemInfo.IsFormatSupported(source.graphicsFormat, GraphicsFormatUsage.ReadPixels)));
                for (int mode = 0; mode < 4; mode++)
                {
                    logs.Clear();
                    AsyncGPUReadbackRequest request;
                    switch (mode)
                    {
                        case 0: request = AsyncGPUReadback.Request(source, 0); break;
                        case 1: request = AsyncGPUReadback.Request(source, 0, TextureFormat.Alpha8); break;
                        case 2: request = AsyncGPUReadback.Request(source, 0, TextureFormat.RGBA32); break;
                        default: request = AsyncGPUReadback.Request(source, 0, TextureFormat.RGBAFloat); break;
                    }
                    request.WaitForCompletion();
                    string key = at + "." + mode;
                    Value(key + ".state", Q(request.hasError + ":" + request.width + ":" + request.height + ":" + request.depth + ":" + request.layerDataSize));
                    if (!request.hasError)
                    {
                        var data = request.GetData<byte>();
                        var hex = new System.Text.StringBuilder();
                        for (int i = 0; i < Math.Min(16, data.Length); i++) hex.Append(data[i].ToString("x2"));
                        Value(key + ".data", Q(hex.ToString()));
                    }
                    Value(key + ".logs", L(logs));
                }
            }
            finally { DestroyImmediate(source); }
        }
    }

}
