using Unbound.Runtime;

namespace UnityEngine.UIElements
{
    public static partial class RuntimePanelUtils
    {
        public static void ResetDynamicAtlas(this IPanel panel)
        {
            if (!Compat.DynamicPanelAtlasMeasured) { Stubs.NotImplemented("UnityEngine.UIElements.RuntimePanelUtils.ResetDynamicAtlas"); return; }
            (panel as BaseVisualElementPanel)?.uaAtlas?.Reset();
        }
        public static void SetTextureDirty(this IPanel panel, Texture2D texture)
        {
            if (!Compat.DynamicPanelAtlasMeasured) { Stubs.NotImplemented("UnityEngine.UIElements.RuntimePanelUtils.SetTextureDirty"); return; }
            (panel as BaseVisualElementPanel)?.uaAtlas?.SetDirty(texture);
        }
    }
}
