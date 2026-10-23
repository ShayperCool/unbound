namespace UnityEngine.Rendering
{
    /// <summary>The graphics settings of a built-in render pipeline project: no scriptable pipeline.</summary>
    public sealed partial class GraphicsSettings : Object
    {
        GraphicsSettings() { }

        public static RenderPipelineAsset currentRenderPipeline => null;

        [System.Obsolete("renderPipelineAsset has been deprecated. Use defaultRenderPipeline instead (UnityUpgradable) -> defaultRenderPipeline")]
        public static RenderPipelineAsset renderPipelineAsset { get => null; set => UnsupportedPipeline(value); }

        public static RenderPipelineAsset defaultRenderPipeline { get => null; set => UnsupportedPipeline(value); }

        public static bool lightsUseLinearIntensity { get; set; }
        public static bool lightsUseColorTemperature { get; set; }
        public static bool useScriptableRenderPipelineBatching { get; set; }
        public static TransparencySortMode transparencySortMode { get; set; }
        public static Vector3 transparencySortAxis { get; set; } = new Vector3(0f, 0f, 1f);

        static void UnsupportedPipeline(RenderPipelineAsset asset)
        {
            if (asset != null)
                Debug.LogError("[Unbound] Scriptable render pipelines are not supported; the built-in pipeline stays active.");
        }
    }

    public abstract partial class RenderPipelineAsset : ScriptableObject
    {
        protected RenderPipelineAsset() { }
    }
}
