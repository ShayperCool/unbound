using System;
using UnityEditorInternal;

namespace UnityEditor
{
    internal sealed partial class TagManager : ProjectSettingsBase
    {
        internal static Action onRenderingLayersChanged;

        TagManager() { }

        internal static int GetDefinedLayerCount()
        {
            int count = 0;
            var layers = Unbound.Runtime.TagManager.Layers;
            for (int i = 0; i < layers.Length; i++)
                if (!string.IsNullOrEmpty(layers[i])) count++;
            return count;
        }

        internal static void GetDefinedLayers(ref string[] layerNames, ref int[] layerValues)
        {
            int count = GetDefinedLayerCount();
            if (layerNames == null) layerNames = new string[count];
            else if (layerNames.Length != count) Array.Resize(ref layerNames, count);
            if (layerValues == null) layerValues = new int[count];
            else if (layerValues.Length != count) Array.Resize(ref layerValues, count);

            var layers = Unbound.Runtime.TagManager.Layers;
            int outIndex = 0;
            for (int i = 0; i < layers.Length && outIndex < count; i++)
            {
                if (string.IsNullOrEmpty(layers[i])) continue;
                layerNames[outIndex] = layers[i];
                layerValues[outIndex] = 1 << i;
                outIndex++;
            }
        }
    }
}
