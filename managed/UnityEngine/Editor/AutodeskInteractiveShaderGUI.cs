using UnityEngine;
using Unbound.Runtime;

namespace UnityEditor
{
    /// <summary>The built-in editor for Autodesk Interactive materials.</summary>
    internal partial class AutodeskInteractiveShaderGUI : ShaderGUI
    {
        public AutodeskInteractiveShaderGUI() { }

        public override void ValidateMaterial(Material material)
        {
            if (Compat.DeferredBuiltinMaterialValidationMeasured) StandardShaderGUI.ValidateAutodeskInteractive(material);
            else Stubs.Ignored("UnityEditor.AutodeskInteractiveShaderGUI.ValidateMaterial",
                "is unmeasured for this release. The material keeps its saved settings.");
        }
    }
}
