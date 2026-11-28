using UnityEngine;
using UnityEngine.Rendering;
using Unbound.Runtime;

namespace UnityEditor
{
    public sealed partial class EditorUtility
    {
        [Unbound.ImplementedIn(Compat.PreviewInitializationFamily)]
        internal static void ConfigurePreviewObjectSRP(GameObject go)
        {
            if (!Compat.PreviewInitializationMeasured)
            {
                Stubs.Ignored("UnityEditor.EditorUtility.ConfigurePreviewObjectSRP",
                    "preview setup is unmeasured for this release family");
                return;
            }
            ConfigurePreviewRenderer(go);
        }

        [Unbound.ImplementedIn(Compat.PreviewInitializationFamily)]
        internal static void InitInstantiatedPreviewRecursive(GameObject go)
        {
            if (!Compat.PreviewInitializationMeasured)
            {
                Stubs.Ignored("UnityEditor.EditorUtility.InitInstantiatedPreviewRecursive",
                    "preview setup is unmeasured for this release family");
                return;
            }
            InitializePreviewHierarchy(go);
        }

        static void InitializePreviewHierarchy(GameObject go)
        {
            go.hideFlags = HideFlags.HideAndDontSave;
            ConfigurePreviewRenderer(go);
            foreach (Transform child in go.transform)
                InitializePreviewHierarchy(child.gameObject);
        }

        static void ConfigurePreviewRenderer(GameObject go)
        {
            if (!GraphicsSettings.isScriptableRenderPipelineEnabled) return;
            go.ThrowIfDestroyed();
            if (go.TryGetComponent<Renderer>(out var renderer)) renderer.lightProbeUsage = LightProbeUsage.Off;
        }
    }
}
