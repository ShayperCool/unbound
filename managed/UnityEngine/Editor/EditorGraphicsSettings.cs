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
            // As the Editor: the asset gets the settings its pipeline supports before it is registered.
            if (newSettings != null) PopulateRenderPipelineGraphicsSettings(newSettings);
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

        static BatchRendererGroupStrippingMode? s_BrgStripping;

        /// <summary>The project's "BatchRendererGroup Variants" setting (GraphicsSettings.asset
        /// <c>m_BrgStripping</c>, read once; without it, as a new project's,
        /// KeepIfEntitiesGraphics).</summary>
        public static BatchRendererGroupStrippingMode batchRendererGroupShaderStrippingMode => s_BrgStripping ??= ReadBrgStripping();

        /// <summary>GraphicsSettings' class ID in serialized files.</summary>
        const int GraphicsSettingsClassId = 30;

        static BatchRendererGroupStrippingMode ReadBrgStripping()
        {
            try
            {
                if (Unbound.Runtime.Native.IsReady && !string.IsNullOrEmpty(UnityEngine.ProjectInfo.ProjectPath))
                {
                    var path = System.IO.Path.Combine(UnityEngine.ProjectInfo.ProjectPath, "ProjectSettings", "GraphicsSettings.asset");
                    if (System.IO.File.Exists(path))
                        foreach (var doc in Unbound.Runtime.Native.LoadDocuments(path))
                            if (doc.ClassId == GraphicsSettingsClassId) return (BatchRendererGroupStrippingMode)doc.Body.Int("m_BrgStripping");
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"Unbound: reading GraphicsSettings: {e.Message}");
            }
            return BatchRendererGroupStrippingMode.KeepIfEntitiesGraphics;
        }
    }

    public enum BatchRendererGroupStrippingMode
    {
        KeepIfEntitiesGraphics = 0,
        StripAll = 1,
        KeepAll = 2,
    }
}
