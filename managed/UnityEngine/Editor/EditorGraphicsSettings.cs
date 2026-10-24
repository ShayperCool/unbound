using System;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace UnityEditor.Rendering
{
    /// <summary>The project's per-pipeline global settings assets (GraphicsSettings' map).</summary>
    public sealed partial class EditorGraphicsSettings
    {
        public static void SetRenderPipelineGlobalSettingsAsset(Type renderPipelineType, RenderPipelineGlobalSettings newSettings)
        {
#pragma warning disable CS0618
            if (newSettings != null) GraphicsSettings.RegisterRenderPipelineSettings(renderPipelineType, newSettings);
            else GraphicsSettings.UnregisterRenderPipelineSettings(renderPipelineType);
#pragma warning restore CS0618
        }

        public static void SetRenderPipelineGlobalSettingsAsset<T>(RenderPipelineGlobalSettings newSettings) where T : RenderPipeline =>
            SetRenderPipelineGlobalSettingsAsset(typeof(T), newSettings);

        public static RenderPipelineGlobalSettings GetRenderPipelineGlobalSettingsAsset(Type renderPipelineType) =>
            GraphicsSettings.GetSettingsForRenderPipeline(renderPipelineType);

        public static RenderPipelineGlobalSettings GetRenderPipelineGlobalSettingsAsset<T>() where T : RenderPipeline =>
            GetRenderPipelineGlobalSettingsAsset(typeof(T));

        public static bool TryGetRenderPipelineSettingsForPipeline<TSettings, TPipeline>(out TSettings settings)
            where TSettings : class, IRenderPipelineGraphicsSettings where TPipeline : RenderPipeline =>
            TryGetRenderPipelineSettingsForPipeline(typeof(TPipeline), out settings);

        public static bool TryGetRenderPipelineSettingsForPipeline<TSettings>(Type renderPipelineType, out TSettings settings)
            where TSettings : class, IRenderPipelineGraphicsSettings
        {
            settings = null;
            var asset = GetRenderPipelineGlobalSettingsAsset(renderPipelineType);
            if (asset == null || !asset.TryGet(typeof(TSettings), out var found)) return false;
            settings = found as TSettings;
            return settings != null;
        }

        public static TSettingsInterfaceType[] GetRenderPipelineSettingsFromInterface<TSettingsInterfaceType>()
            where TSettingsInterfaceType : class, IRenderPipelineGraphicsSettings
        {
            TryGetRenderPipelineSettingsFromInterface<TSettingsInterfaceType>(out var settings);
            return settings ?? Array.Empty<TSettingsInterfaceType>();
        }

        public static bool TryGetFirstRenderPipelineSettingsFromInterface<TSettingsInterfaceType>(out TSettingsInterfaceType settings)
            where TSettingsInterfaceType : class, IRenderPipelineGraphicsSettings
        {
            settings = null;
            return GraphicsSettings.currentRenderPipelineGlobalSettings is { } asset && asset.TryGetFirstSettingsImplementingInterface(out settings);
        }

        public static bool TryGetRenderPipelineSettingsFromInterface<TSettingsInterfaceType>(out TSettingsInterfaceType[] settings)
            where TSettingsInterfaceType : class, IRenderPipelineGraphicsSettings
        {
            settings = null;
            if (GraphicsSettings.currentRenderPipelineGlobalSettings is not { } asset) return false;
            if (!asset.GetSettingsImplementingInterface<TSettingsInterfaceType>(out var list)) return false;
            settings = list.ToArray();
            return true;
        }

        public static bool TryGetRenderPipelineSettingsFromInterfaceForPipeline<TSettingsInterfaceType, TPipeline>(out TSettingsInterfaceType[] settings)
            where TSettingsInterfaceType : class, IRenderPipelineGraphicsSettings where TPipeline : RenderPipeline =>
            TryGetRenderPipelineSettingsFromInterfaceForPipeline(typeof(TPipeline), out settings);

        public static bool TryGetRenderPipelineSettingsFromInterfaceForPipeline<TSettingsInterfaceType>(Type renderPipelineType, out TSettingsInterfaceType[] settings)
            where TSettingsInterfaceType : class, IRenderPipelineGraphicsSettings
        {
            settings = null;
            var asset = GetRenderPipelineGlobalSettingsAsset(renderPipelineType);
            if (asset == null || !asset.GetSettingsImplementingInterface<TSettingsInterfaceType>(out var list)) return false;
            settings = list.ToArray();
            return true;
        }

        internal static void ForEachPipelineSettings(Action<RenderPipelineGlobalSettings> action)
        {
            if (action == null) return;
            foreach (var asset in GraphicsSettings.AllGlobalSettings()) action(asset);
        }
    }
}
