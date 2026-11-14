using System;
using UnityEngine;
using UnityEngine.Rendering;

static partial class ExternalControls
{
    static void ExtendedControls(Action<string, string> row)
    {
        // Initialize every source mip on the GPU, independently of CPU texture uploads.
        for (int kind = 0; kind < 2; kind++)
        {
            var previous = RenderTexture.active;
            var source = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                dimension = kind == 0 ? TextureDimension.Tex2D : TextureDimension.Cube,
                useMipMap = true, autoGenerateMips = false,
            };
            source.Create();
            for (int mip = 0; mip < 4; mip++)
            for (int face = 0; face < (kind == 0 ? 1 : 6); face++)
            {
                Graphics.SetRenderTarget(source, mip, kind == 0 ? CubemapFace.Unknown : (CubemapFace)face);
                GL.Clear(false, true, new Color((20 + 50 * mip) / 255f, 71 / 255f, 91 / 255f, 1));
            }
            RenderTexture.active = previous;
            string key = "externalGpuMips." + kind;
            row(key + ".source0", View(source, kind, false));
            row(key + ".source1", View(source, kind, false, 1));
            IntPtr pointer = source.GetNativeTexturePtr();
            foreach (bool chain in new[] { true, false })
            {
                Texture wrapper = kind == 0 ? Texture2D.CreateExternalTexture(8, 8, TextureFormat.RGBA32, chain, true, pointer)
                    : (Texture)Cubemap.CreateExternalTexture(8, TextureFormat.RGBA32, chain, pointer);
                string item = key + "." + (chain ? "chain" : "single");
                row(item + ".dimensions", View(wrapper, kind, false, 0, true));
                row(item + ".sample0", View(wrapper, kind, false));
                row(item + ".sample1", View(wrapper, kind, false, 1));
                if (chain)
                {
                    row(item + ".whole1", Pixels(wrapper, kind == 0 ? 0 : 3, 4, 4, false, 1));
                    row(item + ".region1", Pixels(wrapper, kind == 0 ? 0 : 3, 4, 4, true, 1));
                }
                UnityEngine.Object.DestroyImmediate(wrapper);
            }
            // Safe reads of the storage despite different public size metadata.
            Texture smaller = kind == 0 ? Texture2D.CreateExternalTexture(4, 4, TextureFormat.RGBA32, true, true, pointer)
                : (Texture)Cubemap.CreateExternalTexture(4, TextureFormat.RGBA32, true, pointer);
            row(key + ".mismatch.meta", Q(smaller.width + ":" + smaller.mipmapCount + ":" + smaller.graphicsFormat));
            row(key + ".mismatch.dimensions", View(smaller, kind, false, 0, true));
            row(key + ".mismatch.sample1", View(smaller, kind, false, 1));
            UnityEngine.Object.DestroyImmediate(smaller);
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(source);
        }
        if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Vulkan) return;
        var fence = new RenderTexture(1, 1, 0); fence.Create();
        NativeGfxVk_SetTexture(0, fence.GetNativeTexturePtr());
        var formats = new[] { TextureFormat.RGBA32, TextureFormat.RGBA32, TextureFormat.R8, TextureFormat.RG16,
            TextureFormat.R16, TextureFormat.RHalf, TextureFormat.RGHalf, TextureFormat.RGBAHalf,
            TextureFormat.RFloat, TextureFormat.RGFloat, TextureFormat.RGBAFloat };
        for (int index = 0; index < formats.Length; index++)
        {
            int kind = index + 3;
            NativeGfxVk_ExternalPrepare(kind);
            GL.IssuePluginEvent(NativeGfxVk_ExternalEvent(), NativeGfxVk_EventBase() + 1);
            Pixels(fence, 0, 1, 1);
            IntPtr pointer = NativeGfxVk_ExternalPtr(kind);
            string key = "externalVkFormats." + formats[index] + (index == 1 ? ".srgb" : ".linear");
            row(key + ".created", B(pointer != IntPtr.Zero));
            if (pointer == IntPtr.Zero) continue;
            var wrapper = Texture2D.CreateExternalTexture(4, 4, formats[index], true, index != 1, pointer);
            row(key + ".dimensions", View(wrapper, 0, false, 0, true));
            row(key + ".sample0", View(wrapper, 0, false));
            row(key + ".sample1", View(wrapper, 0, false, 1));
            UnityEngine.Object.DestroyImmediate(wrapper);
            var again = Texture2D.CreateExternalTexture(4, 4, formats[index], true, index != 1, pointer);
            row(key + ".after", View(again, 0, false, 2));
            UnityEngine.Object.DestroyImmediate(again);
        }
        // Readbacks above complete all uses before the plugin releases its storage.
        NativeGfxVk_ExternalDestroy();
        UnityEngine.Object.DestroyImmediate(fence);
    }
}
