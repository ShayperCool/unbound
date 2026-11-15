using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

static partial class ExternalControls
{
    public static void ForeignStorageControls(Action<string,string> row, Action<Action> readbackScope)
    {
        const int FirstBgraSource = 28;
        var previous = RenderTexture.active;
        var fence = new RenderTexture(1, 1, 0);
        fence.Create();
        NativeGfxVk_SetTexture(0, fence.GetNativeTexturePtr());
        for (int index = 0; index < 2; index++)
        {
            bool linear = index == 0;
            NativeGfxVk_ExternalPrepare(FirstBgraSource + index);
            GL.IssuePluginEvent(NativeGfxVk_ExternalEvent(), NativeGfxVk_EventBase() + 1);
            Pixels(fence, 0, 1, 1);
            IntPtr pointer = NativeGfxVk_ExternalPtr(FirstBgraSource + index);
            string key = "externalVkFormats.BGRA32." + (linear ? "linear" : "srgb");
            row(key + ".created", B(pointer != IntPtr.Zero));
            if (pointer == IntPtr.Zero) continue;
            var wrapper = Texture2D.CreateExternalTexture(4, 4, TextureFormat.BGRA32, true, linear, pointer);
            row(key + ".dimensions", View(wrapper, 0, false, 0, true));
            row(key + ".sample0", View(wrapper, 0, false));
            row(key + ".sample1", View(wrapper, 0, false, 1));
            row(key + ".metadata", Q(wrapper.graphicsFormat + ":" + wrapper.mipmapCount));
            row(key + ".imageSame", B(NativeGfxVk_SameImage(pointer, wrapper.GetNativeTexturePtr()) != 0));
            UnityEngine.Object.DestroyImmediate(wrapper);
            var again = Texture2D.CreateExternalTexture(4, 4, TextureFormat.BGRA32, true, linear, pointer);
            row(key + ".after", View(again, 0, false, 2));
            UnityEngine.Object.DestroyImmediate(again);
        }
        ForeignStorageCopyControls(row, fence, readbackScope);
        NativeGfxVk_ExternalDestroy();
        RenderTexture.active = previous;
        UnityEngine.Object.DestroyImmediate(fence);
    }

    static void ForeignStorageCopyControls(Action<string,string> row, RenderTexture fence, Action<Action> readbackScope)
    {
        const int FirstBgraSource = 28, FirstBgraDestination = 30;
        const int SharedExponentSource = 32, SharedExponentDestination = 33;
        for (int index = 0; index < 3; index++)
        {
            bool packed = index == 2;
            bool linear = packed || index == 0;
            TextureFormat format = packed ? TextureFormat.RGB9e5Float : TextureFormat.BGRA32;
            string key = packed ? "externalVkPackedCopy.RGB9e5Float" : "externalVkBgraCopy." + (linear ? "linear" : "srgb");
            int sourceKind = packed ? SharedExponentSource : FirstBgraSource + index;
            int destinationKind = packed ? SharedExponentDestination : FirstBgraDestination + index;
            if (packed)
            {
                NativeGfxVk_ExternalPrepare(sourceKind);
                GL.IssuePluginEvent(NativeGfxVk_ExternalEvent(), NativeGfxVk_EventBase() + 1);
                Pixels(fence, 0, 1, 1);
            }
            NativeGfxVk_ExternalPrepare(destinationKind);
            GL.IssuePluginEvent(NativeGfxVk_ExternalEvent(), NativeGfxVk_EventBase() + 1);
            Pixels(fence, 0, 1, 1);
            IntPtr sourcePointer = NativeGfxVk_ExternalPtr(sourceKind);
            IntPtr destinationPointer = NativeGfxVk_ExternalPtr(destinationKind);
            row(key + ".created", B(sourcePointer != IntPtr.Zero && destinationPointer != IntPtr.Zero));
            if (sourcePointer == IntPtr.Zero || destinationPointer == IntPtr.Zero) continue;
            var source = Texture2D.CreateExternalTexture(4, 4, format, true, linear, sourcePointer);
            var destination = Texture2D.CreateExternalTexture(4, 4, format, true, linear, destinationPointer);
            row(key + ".before", View(destination, 0, false));
            row(key + ".source", View(source, 0, false));
            var messages = new System.Collections.Generic.List<string>();
            Application.LogCallback logger = (message, stack, type) => messages.Add(type + ": " + message);
            Application.logMessageReceived += logger;
            try
            {
                Graphics.CopyTexture(source, 0, 0, destination, 0, 0);
                row(key + ".after", View(destination, 0, false));
                row(key + ".mip1", View(destination, 0, false, 1));
                row(key + ".sourceAfter", View(source, 0, false));
            }
            finally { Application.logMessageReceived -= logger; }
            row(key + ".logs", Q(string.Join("\n", messages)));
            row(key + ".metadata", Q(source.graphicsFormat + ":" + source.mipmapCount));
            row(key + ".imageSame", B(NativeGfxVk_SameImage(sourcePointer, source.GetNativeTexturePtr()) != 0));
            UnityEngine.Object.DestroyImmediate(source);
            var again = Texture2D.CreateExternalTexture(4, 4, format, true, linear, sourcePointer);
            row(key + ".rewrappedMip2", View(again, 0, false, 2));
            var readbackMessages = new System.Collections.Generic.List<string>();
            Application.LogCallback readbackLogger = (message, stack, type) => readbackMessages.Add(type + ": " + message);
            Application.logMessageReceived += readbackLogger;
            try
            {
                readbackScope(() =>
                {
                    if (packed)
                    {
                        var request = AsyncGPUReadback.Request(again, 0, TextureFormat.RGBAFloat);
                        request.WaitForCompletion();
                        row(key + ".readbackError", B(request.hasError));
                        if (!request.hasError)
                        {
                            var value = request.GetData<Color>()[0];
                            row(key + ".readback", Q(string.Join(",", new[] { value.r, value.g, value.b, value.a }
                                .Select(v => v.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)).ToArray())));
                        }
                    }
                    else
                    {
                        var request = AsyncGPUReadback.Request(again, 0, linear ? GraphicsFormat.R8G8B8A8_UNorm : GraphicsFormat.R8G8B8A8_SRGB);
                        request.WaitForCompletion();
                        row(key + ".readbackError", B(request.hasError));
                        if (!request.hasError)
                        {
                            var bytes = request.GetData<byte>();
                            row(key + ".readback", Q(bytes[0].ToString("x2") + bytes[1].ToString("x2")
                                + bytes[2].ToString("x2") + bytes[3].ToString("x2")));
                        }
                    }
                });
            }
            finally { Application.logMessageReceived -= readbackLogger; }
            row(key + ".readback.logs", Q(string.Join("\n", readbackMessages)));
            UnityEngine.Object.DestroyImmediate(again);
            UnityEngine.Object.DestroyImmediate(destination);
        }
    }
}
