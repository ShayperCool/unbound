using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEditor.Rendering
{
    /// <summary>
    /// Editor helpers render pipelines call, also from their runtime code in Play mode: a
    /// pipeline asset type's pipeline type, the project's rendering layer names
    /// (<see cref="RenderingLayerMask"/>, edited in memory), the limit a pipeline puts on them.
    /// The C# rules are Unity's; matches the Unity 6000.0.71f1 Editor, tests/projects/graphicsgaps
    /// layers.*.
    /// </summary>
    public static partial class RenderPipelineEditorUtility
    {
        static readonly Dictionary<Type, Type> s_RenderPipelineAssetToRenderPipelineType = new Dictionary<Type, Type>();

        public static Type[] GetDerivedTypesSupportedOnCurrentPipeline<T>() =>
            TypeCache.GetTypesDerivedFrom<T>()
                .Where(t => t.GetCustomAttribute<SupportedOnRenderPipelineAttribute>()?.isSupportedOnCurrentPipeline ?? false)
                .ToArray();

        /// <summary>The pipeline type of a RenderPipelineAsset type: its RenderPipelineAsset&lt;T&gt;
        /// argument, else the pipelineType of an instance made for the question (an abstract type
        /// warns and throws, as in the Editor); null for a type that is no pipeline asset.</summary>
        public static Type GetPipelineTypeFromPipelineAssetType(Type pipelineAssetType)
        {
            if (!typeof(RenderPipelineAsset).IsAssignableFrom(pipelineAssetType)) return null;
            if (s_RenderPipelineAssetToRenderPipelineType.TryGetValue(pipelineAssetType, out var known)) return known;
            for (var type = pipelineAssetType; type != null; type = type.BaseType)
            {
                if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(RenderPipelineAsset<>)) continue;
                var argument = type.GetGenericArguments()[0];
                s_RenderPipelineAssetToRenderPipelineType[pipelineAssetType] = argument;
                return argument;
            }
            var asset = ScriptableObject.CreateInstance(pipelineAssetType) as RenderPipelineAsset;
            // An abstract asset type makes no instance (warned): the Editor then throws, in Mono's words.
            if (asset == null) throw Unbound.Runtime.MonoExceptions.NullReference();
            var pipelineType = asset.pipelineType;
            UnityEngine.Object.DestroyImmediate(asset);
            s_RenderPipelineAssetToRenderPipelineType[pipelineAssetType] = pipelineType;
            return pipelineType;
        }

        public static bool TrySetRenderingLayerName(int index, string name) => RenderingLayerMask.TrySetName(index, name);

        public static bool TryAddRenderingLayerName(string name) => RenderingLayerMask.TryAddName(name);

        public static bool TryRemoveLastRenderingLayerName() => RenderingLayerMask.TryRemoveLastName();

        /// <summary>The active pipeline's limit (its RenderingLayersLimitSettings), else 32.</summary>
        internal static int GetActiveMaxRenderingLayers() =>
            EditorGraphicsSettings.TryGetRenderPipelineSettingsFromInterface<RenderingLayersLimitSettings>(out var settings) && settings.Length != 0
                ? settings[0].maxSupportedRenderingLayers
                : 32;

        /// <summary>Each configured pipeline's limit and name, once per pipeline type.</summary>
        internal static List<(int, string)> GetMaxRenderingLayersFromSettings()
        {
            var seen = new HashSet<Type>();
            var list = new List<(int, string)>();
            foreach (var asset in GraphicsSettings.allConfiguredRenderPipelines)
            {
                var pipelineType = GetPipelineTypeFromPipelineAssetType(asset.GetType());
                if (pipelineType == null || seen.Contains(pipelineType)) continue;
                if (!EditorGraphicsSettings.TryGetRenderPipelineSettingsFromInterfaceForPipeline<RenderingLayersLimitSettings>(pipelineType, out var settings) || settings.Length == 0)
                    continue;
                seen.Add(pipelineType);
                list.Add((settings[0].maxSupportedRenderingLayers, asset.pipelineType.Name));
            }
            return list;
        }

        /// <summary>The names and values a rendering layer mask field offers: the defined layers,
        /// limited to the active pipeline's count (a selected layer past it stays), and an
        /// "Unnamed Layer n" for each set bit no defined layer has.</summary>
        internal static (string[], int[]) GetRenderingLayerNamesAndValuesForMask(uint currentMask)
        {
            var names = RenderingLayerMask.GetDefinedRenderingLayerNames();
            var values = RenderingLayerMask.GetDefinedRenderingLayerValues();
            int max = GetActiveMaxRenderingLayers();
            bool everything = currentMask == uint.MaxValue;
            uint unnamed = 0;
            if (!everything)
            {
                unnamed = currentMask;
                foreach (var v in values)
                    if ((currentMask & (uint)v) != 0) unnamed &= ~(uint)v;
            }
            if (max >= 32 && unnamed == 0) return (names, values);
            var keptNames = new List<string>();
            var keptValues = new List<int>();
            for (int i = 0; i < values.Length; i++)
            {
                uint value = (uint)values[i];
                // Within the limit: the value's bit index is below `max`.
                bool inRange = max >= 32 || value < (1u << max);
                if (inRange || (!everything && (value & currentMask) != 0))
                {
                    keptNames.Add(names[i]);
                    keptValues.Add(values[i]);
                }
            }
            for (int bit = 0; bit < 32; bit++)
            {
                if ((unnamed & (1u << bit)) == 0) continue;
                int at = keptValues.FindIndex(v => (uint)v > (1u << bit));
                if (at < 0) at = keptValues.Count;
                keptNames.Insert(at, $"Unnamed Layer {bit}");
                keptValues.Insert(at, (int)(1u << bit));
            }
            return (keptNames.ToArray(), keptValues.ToArray());
        }

        internal static bool DoesMaskContainRenderingLayersOutsideOfMaxBitCount(uint mask, int bitCount) =>
            bitCount != 32 && mask != uint.MaxValue && bitCount >= 0 && bitCount < 32 && mask >= (1u << bitCount);

        internal static string GetOutsideOfMaxBitCountWarningMessage(int bitCount) =>
            $"Current mask contains layers outside of a supported range by active Render Pipeline. The active Render Pipeline only supports up to {bitCount} layers. Rendering Layers above {bitCount} are ignored.";
    }

    /// <summary>The number of rendering layers a pipeline supports (2 to 32; another answer of the
    /// pipeline is 32).</summary>
    public abstract partial class RenderingLayersLimitSettings : IRenderPipelineGraphicsSettings
    {
        internal enum Version { Initial }

        [HideInInspector]
        [SerializeField]
        Version m_Version = Version.Initial;

        protected RenderingLayersLimitSettings() { }

        public int version => (int)m_Version;

        protected abstract int maxRenderingLayersForPipeline { get; }

        public int maxSupportedRenderingLayers
        {
            get
            {
                int max = maxRenderingLayersForPipeline;
                return max > 1 && max <= 32 ? max : 32;
            }
        }
    }

    /// <summary>Editing a pipeline's graphics settings (the ones its global settings asset holds).</summary>
    public static partial class RenderPipelineGraphicsSettingsEditorUtility
    {
        public static void ForEachFieldOfType<T>(this IRenderPipelineResources resource, Action<T> callback,
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        {
            if (resource == null) return;
            foreach (var field in resource.GetType().GetFields(flags))
                if (field.GetValue(resource) is T value) callback(value);
        }

        /// <summary>Puts the settings in place of the pipeline's settings of their type (nothing
        /// for null settings, a type that is no pipeline, or settings the asset does not hold):
        /// the same instance is then what GraphicsSettings answers (matches the Unity 6000.0.71f1
        /// Editor, tests/projects/graphicsgaps rebind.*).</summary>
        public static void Rebind(IRenderPipelineGraphicsSettings settings, Type renderPipelineType)
        {
            if (settings == null || renderPipelineType == null || !typeof(RenderPipeline).IsAssignableFrom(renderPipelineType)) return;
            var global = EditorGraphicsSettings.GetRenderPipelineGlobalSettingsAsset(renderPipelineType);
            if (global == null) return;
            global.Replace(settings);
        }
    }

    /// <summary>What the Scene view's filters and hidden objects leave of a list of renderers (the
    /// GPU Resident Drawer asks while it draws the Scene view, which Unbound does not show).</summary>
    public static partial class EditorCameraUtils
    {
        /// <summary>True for each renderer: no Scene view search filters any (hiding does not
        /// change the answer: tests/projects/graphicsgaps cameraUtils.filter*).</summary>
        public static void GetRenderersFilteringResults(ReadOnlySpan<int> rendererIDs, Span<bool> results)
        {
            if (rendererIDs.Length != results.Length) throw new ArgumentException("rendererIDs and results NativeArrays don't match in length.");
            for (int i = 0; i < rendererIDs.Length; i++) results[i] = Unbound.Runtime.ObjectRegistry.FindById(rendererIDs[i]) is Renderer;
        }

        /// <summary>A bit per renderer, set for one SceneVisibilityManager hides
        /// (tests/projects/graphicsgaps cameraUtils.hidden*).</summary>
        public static void GetRenderersHiddenResultBits(ReadOnlySpan<int> rendererIDs, Span<ulong> resultBits)
        {
            if ((rendererIDs.Length + 63) / 64 != resultBits.Length)
                throw new ArgumentException("Unexpected resultBits Span length. The expected length of resultBits Span should be equal to the formula: (rendererIDs.Length + 63) / 64");
            resultBits.Clear();
            for (int i = 0; i < rendererIDs.Length; i++)
                if (Unbound.Runtime.ObjectRegistry.FindById(rendererIDs[i]) is Renderer renderer && SceneVisibilityManager.IsHiddenObject(renderer.gameObject))
                    resultBits[i / 64] |= 1ul << (i % 64);
        }
    }
}
