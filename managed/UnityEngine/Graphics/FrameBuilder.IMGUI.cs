using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    internal static partial class FrameBuilder
    {
        /// <summary>
        /// The blending of an IMGUI draw with alphaBlend false. Documented (Unity 6000.0 Scripting
        /// API): GUI.DrawTexture's alphaBlend is "whether to apply alpha blending when drawing the
        /// image (enabled by default)"; GUI.DrawTextureWithTexCoords' "if false, the picture is drawn
        /// on to the display". Unbound's choice (unmeasured): Blend One Zero for color and alpha (the
        /// renderer's Blend Off) with every channel written, which is the UI material's default
        /// _ColorMask (15); the fragment's color and alpha are written as the UI shader outputs them.
        /// </summary>
        internal static readonly RenderTargetBlendState OpaqueImguiBlend = new RenderTargetBlendState(
            ColorWriteMask.All, BlendMode.One, BlendMode.Zero, BlendMode.One, BlendMode.Zero, BlendOp.Add, BlendOp.Add);

        /// <summary>Before an opaque IMGUI draw: the state override in force (a mask's stencil test,
        /// a renderer list's block, or none) with its blend state replaced by
        /// <see cref="OpaqueImguiBlend"/> and nothing else of it changed. Returns the override to put
        /// back with <see cref="EndOpaqueImgui"/> right after the draw, so nothing reaches the draws
        /// or cameras after it, and the shared UI material is never changed.</summary>
        internal static RenderStateBlock? BeginOpaqueImgui()
        {
            var previous = GfxStream.CurrentOverride;
            var block = previous ?? new RenderStateBlock(RenderStateMask.Nothing);
            block.mask |= RenderStateMask.Blend;
            var blend = BlendState.defaultValue;
            blend.blendState0 = OpaqueImguiBlend;
            block.blendState = blend;
            GfxStream.StateOverride(block);
            return previous;
        }

        /// <summary>After an opaque IMGUI draw: the state override from before it.</summary>
        internal static void EndOpaqueImgui(RenderStateBlock? previous) => GfxStream.StateOverride(previous);
    }
}
