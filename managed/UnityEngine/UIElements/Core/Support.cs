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
    public sealed partial class VectorImage : ScriptableObject
    {
        /// <summary>The image's size in points (its vector geometry is drawn scaled to the element).</summary>
        internal Vector2 size;

        public float width => size.x;

        public float height => size.y;
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
