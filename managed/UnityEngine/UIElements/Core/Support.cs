using System;
using System.Collections.Generic;

// Small engine-side pieces UI Toolkit needs from other modules.

namespace UnityEngine
{
    public sealed partial class Texture2D
    {
        float m_UaPixelsPerPoint = 1f;

        /// <summary>Points per pixel of the image (2 for the @2x variant of a UI image).</summary>
        internal float pixelsPerPoint
        {
            get => m_UaPixelsPerPoint;
            set => m_UaPixelsPerPoint = value;
        }
    }

    public sealed partial class Font
    {
        /// <summary>A font of the Editor's resources (UI Toolkit's default theme fonts).</summary>
        internal static Font UaFromEditorResource(long pathId, string name, float fontSize)
        {
            var source = Unbound.Runtime.NativeGlyphSource.Open(new { editorResource = pathId });
            if (source == null) throw new InvalidOperationException($"the font {name} could not be opened");
            using (Unbound.Runtime.ObjectRegistry.AsAssets())
            {
                var font = new Font { m_Name = name, IsPersistentAsset = true };
                font.InitFromSource(source, Mathf.RoundToInt(fontSize), name);
                font.m_UaEditorResourceId = pathId;
                return font;
            }
        }

        /// <summary>The path id of a font of the Editor's resources (its face for TextCore).</summary>
        internal long m_UaEditorResourceId;
    }
}

namespace UnityEngine.UIElements
{
    [Serializable]
    public sealed partial class VectorImage : ScriptableObject
    {
        [SerializeField] internal int version;
        /// <summary>The texture its gradients sample (null for an image without gradients).</summary>
        [SerializeField] internal Texture2D atlas;
        /// <summary>The image's geometry (Painter2D.SaveToVectorImage or an SVG importer writes it):
        /// UIR vertices relative to the image's top-left corner, and triangle indices.</summary>
        [SerializeField] internal VectorImageVertex[] vertices;
        [SerializeField] internal ushort[] indices;
        /// <summary>Where each gradient lies in <see cref="atlas"/> and how it is sampled.</summary>
        [SerializeField] internal GradientSettings[] settings;
        /// <summary>The image's size in points (its vector geometry is drawn scaled to the element).</summary>
        [SerializeField] internal Vector2 size;

        public float width => size.x;

        public float height => size.y;
    }

    /// <summary>A gradient of a VectorImage: its type, how texture coordinates outside 0..1 are
    /// addressed, the radial focus (-1..1) and the texels of its row in the image's atlas.</summary>
    [Serializable]
    internal partial struct GradientSettings
    {
        public GradientType gradientType;
        public AddressMode addressMode;
        public Vector2 radialFocus;
        public RectInt location;
    }

    internal enum GradientType
    {
        Linear,
        Radial,
    }

    internal enum AddressMode
    {
        Wrap,
        Clamp,
        Mirror,
    }

    [Serializable]
    internal partial struct VectorImageVertex
    {
        public Vector3 position;
        public Color32 tint;
        public Vector2 uv;
        public uint settingIndex;
        public Color32 flags;
        public Vector4 circle;
    }

    public partial class IMGUIContainer : VisualElement, IDisposable
    {
        public IMGUIContainer() { isIMGUIContainer = true; }
        [Unbound.EditorFaithful("this container keeps no IMGUI state to release")]
        public void Dispose() { }
    }

    public partial interface IManipulator
    {
        VisualElement target { get; set; }
    }
}

namespace Unbound.UIElements
{
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>Input for player panels (filled in by the event system integration).</summary>
    internal static partial class UaInput
    {
        internal static void Process(DefaultEventSystem system) => ProcessInput(system);

        static partial void ProcessInputImpl(DefaultEventSystem system);

        static void ProcessInput(DefaultEventSystem system) => ProcessInputImpl(system);
    }
}
