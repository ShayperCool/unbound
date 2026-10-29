using System;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>A mask of rendering layers (named in the Tags and Layers settings).</summary>
    [Serializable]
    public partial struct RenderingLayerMask
    {
        internal const int maxRenderingLayerSize = 32;

        /// <summary>The project's rendering layer names, by index (undefined ones empty).</summary>
        internal static string[] s_Names = { "Default" };

        [SerializeField] uint m_Bits;

        public uint value { get => m_Bits; set => m_Bits = value; }

        public static RenderingLayerMask defaultRenderingLayerMask => GraphicsSettings.defaultRenderingLayerMask;

        public static implicit operator uint(RenderingLayerMask mask) => mask.m_Bits;
        public static implicit operator RenderingLayerMask(uint intVal) => new RenderingLayerMask { m_Bits = intVal };
        public static implicit operator int(RenderingLayerMask mask) => (int)mask.m_Bits;
        public static implicit operator RenderingLayerMask(int intVal) => new RenderingLayerMask { m_Bits = (uint)intVal };

        public static string RenderingLayerToName(int layer) => layer >= 0 && layer < s_Names.Length ? s_Names[layer] : "";

        public static int NameToRenderingLayer(string layerName)
        {
            if (string.IsNullOrEmpty(layerName)) return -1;
            return Array.IndexOf(s_Names, layerName);
        }

        public static uint GetMask(params string[] renderingLayerNames) => GetMask((ReadOnlySpan<string>)renderingLayerNames);

        public static uint GetMask(ReadOnlySpan<string> renderingLayerNames)
        {
            uint mask = 0;
            foreach (var name in renderingLayerNames)
            {
                int layer = NameToRenderingLayer(name);
                if (layer >= 0) mask |= 1u << layer;
            }
            return mask;
        }

        public static int GetDefinedRenderingLayerCount()
        {
            int n = 0;
            foreach (var name in s_Names) if (!string.IsNullOrEmpty(name)) n++;
            return n;
        }

        public static int GetLastDefinedRenderingLayerIndex()
        {
            for (int i = s_Names.Length - 1; i >= 0; i--) if (!string.IsNullOrEmpty(s_Names[i])) return i;
            return -1;
        }

        public static uint GetDefinedRenderingLayersCombinedMaskValue()
        {
            uint mask = 0;
            for (int i = 0; i < s_Names.Length && i < 32; i++) if (!string.IsNullOrEmpty(s_Names[i])) mask |= 1u << i;
            return mask;
        }

        public static string[] GetDefinedRenderingLayerNames()
        {
            var names = new List<string>();
            foreach (var name in s_Names) if (!string.IsNullOrEmpty(name)) names.Add(name);
            return names.ToArray();
        }

        public static int[] GetDefinedRenderingLayerValues()
        {
            var values = new List<int>();
            for (int i = 0; i < s_Names.Length; i++) if (!string.IsNullOrEmpty(s_Names[i])) values.Add(1 << i);
            return values.ToArray();
        }

        public static int GetRenderingLayerCount() => s_Names.Length;
    }

    /// <summary>A display's HDR output: Unbound presents standard dynamic range only, so no
    /// display has HDR available.</summary>
    public partial class HDROutputSettings
    {
        public static HDROutputSettings[] displays = { new HDROutputSettings(0) };
        readonly int m_DisplayIndex;
        bool m_Requested;

        internal HDROutputSettings() { }

        internal HDROutputSettings(int displayIndex) => m_DisplayIndex = displayIndex;

        public static HDROutputSettings main => displays[0];

        public void RequestHDRModeChange(bool enabled) => m_Requested = false;

        [Obsolete("SetPaperWhiteInNits is deprecated, please use paperWhiteNits instead.")]
        public static void SetPaperWhiteInNits(float paperWhite) => main.paperWhiteNits = paperWhite;

        [Unbound.EditorFaithful("no HDR display output in the Editor (hdrDisplaySupportFlags None, measured: tests/projects/urp CapsProbe)")]
        public bool active => false;
        [Unbound.EditorFaithful("no HDR display output in the Editor (hdrDisplaySupportFlags None, measured: tests/projects/urp CapsProbe)")]
        public bool available => false;
        public bool automaticHDRTonemapping { get; set; } = true;
        public ColorGamut displayColorGamut => ColorGamut.sRGB;
        public RenderTextureFormat format => RenderTextureFormat.ARGB32;
        public GraphicsFormat graphicsFormat => GraphicsFormat.R8G8B8A8_SRGB;
        public float paperWhiteNits { get; set; } = 160f;
        [Unbound.EditorFaithful("no HDR display output in the Editor (hdrDisplaySupportFlags None, measured: tests/projects/urp CapsProbe)")]
        public int maxFullFrameToneMapLuminance => 0;
        [Unbound.EditorFaithful("no HDR display output in the Editor (hdrDisplaySupportFlags None, measured: tests/projects/urp CapsProbe)")]
        public int maxToneMapLuminance => 0;
        [Unbound.EditorFaithful("no HDR display output in the Editor (hdrDisplaySupportFlags None, measured: tests/projects/urp CapsProbe)")]
        public int minToneMapLuminance => 0;
        public bool HDRModeChangeRequested => m_Requested;
    }
}
