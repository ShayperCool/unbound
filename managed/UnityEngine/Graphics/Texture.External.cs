using System;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    public partial class Texture
    {
        internal IntPtr m_ExternalTexture;
        bool m_ExternalActive, m_ExternalBound;
        ExternalTextureInfo m_ExternalDescription;
        internal bool IsExternalTexture => m_ExternalActive;

        internal void BindExternalTexture(IntPtr pointer, TextureFormat format, bool linear, int depth = 1)
        {
            m_ExternalActive = true;
            m_ExternalTexture = pointer;
            m_ExternalDescription = new ExternalTextureInfo
            {
                Width = m_Width, Height = m_Height, Depth = depth, Format = (int)format,
                MipCount = mipmapCount, Dimension = (int)dimension, Linear = linear ? 1 : 0,
                Flags = Compat.RenderingMeasured ? ExternalTextureInfo.MeasuredDeviceLayout : 0,
            };
            m_ExternalBound = false;
            EnsureExternalUploaded();
        }

        internal unsafe ulong EnsureExternalUploaded()
        {
            if (m_NativeHandle == 0) m_NativeHandle = RenderHandles.Next();
            if (m_ExternalBound || !Native.IsReady) return m_NativeHandle;
            GfxStream.Flush();
            var description = m_ExternalDescription;
            var error = new NativeBuffer();
            m_ExternalBound = true;
            if (Native.Api.ExternalTexture(m_NativeHandle, (ulong)m_ExternalTexture, &description, &error) != 0)
            {
                string reason = error.Length == 0 ? "unknown error" : System.Text.Encoding.UTF8.GetString(error.Data, (int)error.Length);
                Native.Free(error);
                Debug.LogError($"Unbound: the external texture {m_ExternalTexture} cannot be shown: {reason}");
                return m_NativeHandle;
            }
            EmitSamplingProperties();
            if (mipMapBias != 0f) EmitSamplerMetadata();
            return m_NativeHandle;
        }

        internal IntPtr ExternalNativePointer()
        {
            ulong handle = EnsureExternalUploaded();
            return !Native.IsReady || SystemInfo.graphicsDeviceType != GraphicsDeviceType.Vulkan
                ? m_ExternalTexture : NativePlugins.TexturePointer(handle, false);
        }

        internal void CheckExternalCpu(string api, string type = null)
        {
            if (m_ExternalActive)
            {
                string member = type == null ? api : type + "." + api;
                throw new ArgumentException($"{member}: not allowed on native textures. (Texture '{name}')");
            }
        }
    }
}
