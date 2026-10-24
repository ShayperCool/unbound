using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEngine.UIElements
{
    /// <summary>The mesh API of generateVisualContent callbacks.</summary>
    public partial class MeshGenerationContext
    {
        internal VisualElement uaElement;

        internal MeshGenerationContext() { }

        public VisualElement visualElement => uaElement;
    }
}

namespace Unbound.UIElements
{
    /// <summary>Draws panels: screen panels over the scene, panels with a target texture into it.</summary>
    internal static partial class PanelRendering
    {
        internal static void InstallFrameHook() => InstallFrameHookImpl();

        static partial void InstallFrameHookImpl();

        internal static void Render(BaseRuntimePanel panel) => RenderImpl(panel);

        static partial void RenderImpl(BaseRuntimePanel panel);
    }
}
